using System.Globalization;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Recording;

/// <summary>
/// Records test scenes for the phase 3 judge (DECISIONS 024), alongside the watch, which goes on untouched: the
/// camera streams 15 frames a second and the watch uses one every 2 s, so a recording only keeps, once a second, a
/// frame that would have been dropped. One recording at a time, to <c>yyyyMMdd-HHmmssZ-name.vseq</c> in its directory.
/// It stops on its own at its length, or when the phone gets down to <see cref="MinFreeBytes"/> free, so a forgotten
/// one cannot fill the phone. Nothing here throws into the camera thread: a write that fails ends the recording and
/// goes to the log. Thread-safe: the camera calls <see cref="WantsFrame"/> and <see cref="Write"/>, the bot the rest.
/// </summary>
public sealed class Recorder : IDisposable
{
    public static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1);

    /// <summary>Camera frames are shrunk this much on each side: 320x240 to 160x120, enough to try coarser ones later.</summary>
    public const int ReduceFactor = 2;

    /// <summary>The phone had 4.8 GB free (DECISIONS 003); recordings never take it below this.</summary>
    public const long MinFreeBytes = 1_500_000_000;

    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly IWatchLog _log;
    private readonly Func<long?> _freeBytes;
    private readonly object _gate = new();
    private readonly List<RecordingState> _finished = [];
    private RecordingState? _current;
    private SequenceWriter? _writer;
    private DateTimeOffset _nextDue;

    /// <param name="freeBytes">The space left on the phone's storage; null if it cannot be read.</param>
    public Recorder(string directory, TimeProvider clock, IWatchLog log, Func<long?> freeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(freeBytes);
        _directory = directory;
        _clock = clock;
        _log = log;
        _freeBytes = freeBytes;
    }

    /// <summary>The running recording, if any.</summary>
    public RecordingState? Current
    {
        get { lock (_gate) { return _current; } }
    }

    public RecordingStart Start(TimeSpan duration, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            ExpireIfDue(_clock.GetUtcNow());
            if (_current is { } running)
            {
                return new RecordingStart.Busy(running);
            }

            if (_freeBytes() is { } free && free < MinFreeBytes)
            {
                _log.Write(LogKind.Info, $"record: not started, {free} bytes free");
                return new RecordingStart.NoSpace(free);
            }

            var now = _clock.GetUtcNow();
            var file = string.Create(CultureInfo.InvariantCulture, $"{now.UtcDateTime:yyyyMMdd-HHmmss}Z-{name}.vseq");
            _current = new RecordingState(name, Path.Combine(_directory, file), now, duration, 0, 0, null);
            _nextDue = now;
            _log.Write(LogKind.Info, string.Create(CultureInfo.InvariantCulture, $"record: started {name} for {duration.TotalSeconds:0} s"));
            return new RecordingStart.Started(_current);
        }
    }

    /// <summary>Ends the running recording at the owner's request; null if none was running.</summary>
    public RecordingState? Stop()
    {
        lock (_gate)
        {
            ExpireIfDue(_clock.GetUtcNow());
            return _current is null ? null : Finish(RecordingEnd.Stopped, report: false);
        }
    }

    /// <summary>
    /// For the camera, on every frame: whether this one should be recorded. Cheap when nothing is recording. Also ends
    /// a recording that has run its length or has run out of room.
    /// </summary>
    public bool WantsFrame()
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return false;
            }

            var now = _clock.GetUtcNow();
            if (ExpireIfDue(now) || now < _nextDue)
            {
                return false;
            }

            if (_freeBytes() is { } free && free < MinFreeBytes)
            {
                _log.Write(LogKind.Info, $"record: stopping, {free} bytes free");
                Finish(RecordingEnd.LowSpace, report: true);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Records a camera frame: its Y plane (<paramref name="width"/> x <paramref name="height"/>, rows
    /// <paramref name="rowStride"/> apart), shrunk by <see cref="ReduceFactor"/>, and its JPEG. Call after
    /// <see cref="WantsFrame"/> said yes; otherwise it does nothing.
    /// </summary>
    public void Write(ReadOnlySpan<byte> yPlane, int width, int height, int rowStride, int sensorOrientation, ReadOnlySpan<byte> jpeg)
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            var now = _clock.GetUtcNow();
            if (now < _nextDue)
            {
                return;
            }

            try
            {
                var luma = LumaImage.Reduce(yPlane, width, height, rowStride, ReduceFactor);
                _writer ??= Create(new SequenceHeader(_current.Name, _current.StartedAt, luma.Width, luma.Height, width, height, sensorOrientation, FrameInterval));
                _writer.Write(now, luma, jpeg);
                _current = _current with { Frames = _writer.FramesWritten, Bytes = _writer.BytesWritten };

                // One a second on average; after a gap (camera lost) start counting again from now, without catching up.
                _nextDue += FrameInterval;
                if (_nextDue <= now)
                {
                    _nextDue = now + FrameInterval;
                }
            }
            catch (Exception error)
            {
                _log.Write(LogKind.Error, $"record: write failed: {error.GetType().Name}: {error.Message}");
                Finish(RecordingEnd.Failed, report: true);
            }
        }
    }

    /// <summary>
    /// The recordings that ended on their own since the last call, for the bot to tell the owner; also ends one that
    /// has run its length even if the camera stopped sending frames.
    /// </summary>
    public IReadOnlyList<RecordingState> TakeFinished()
    {
        lock (_gate)
        {
            ExpireIfDue(_clock.GetUtcNow());
            var finished = _finished.ToList();
            _finished.Clear();
            return finished;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_current is not null)
            {
                Finish(RecordingEnd.Interrupted, report: false);
            }
        }
    }

    private bool ExpireIfDue(DateTimeOffset now)
    {
        if (_current is null || now < _current.EndsAt)
        {
            return false;
        }

        Finish(RecordingEnd.Completed, report: true);
        return true;
    }

    private SequenceWriter Create(SequenceHeader header)
    {
        Directory.CreateDirectory(_directory);
        var stream = new FileStream(_current!.Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try
        {
            return new SequenceWriter(stream, header);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private RecordingState Finish(RecordingEnd end, bool report)
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception error)
        {
            _log.Write(LogKind.Error, $"record: close failed: {error.GetType().Name}: {error.Message}");
        }

        var finished = _current! with { End = end };
        _writer = null;
        _current = null;
        _log.Write(LogKind.Info, $"record: finished {finished.Name}: {finished.Frames} frames, {finished.Bytes} bytes, {end.ToString().ToLowerInvariant()}");
        if (report)
        {
            _finished.Add(finished);
        }

        return finished;
    }
}
