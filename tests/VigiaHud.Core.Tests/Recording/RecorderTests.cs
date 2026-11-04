using Microsoft.Extensions.Time.Testing;
using VigiaHud.Core.Logging;
using VigiaHud.Core.Recording;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Recording;

/// <summary>
/// The recorder fed like the camera feeds it: a frame every 1/15 s, each one offered and only the wanted ones written.
/// </summary>
public sealed class RecorderTests : IDisposable
{
    private const int Width = 8;
    private const int Height = 6;
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 15, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan CameraFrame = TimeSpan.FromSeconds(1.0 / 15);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly MemoryLog _log = new();
    private long? _free = 4_800_000_000;
    private readonly Recorder _recorder;

    public RecorderTests()
    {
        _recorder = new Recorder(_directory, _clock, _log, () => _free);
    }

    public void Dispose()
    {
        _recorder.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Nothing_is_wanted_while_nothing_records()
    {
        Assert.False(_recorder.WantsFrame());
        Assert.Null(_recorder.Current);
    }

    [Fact]
    public void A_recording_keeps_one_frame_a_second_for_its_whole_length()
    {
        var started = Assert.IsType<RecordingStart.Started>(_recorder.Start(TimeSpan.FromSeconds(10), "salida-1"));

        var written = Stream(TimeSpan.FromSeconds(12));

        Assert.Equal(Path.Combine(_directory, "20261104-153000Z-salida-1.vseq"), started.Recording.Path);
        Assert.Equal(10, written);
        var finished = Assert.Single(_recorder.TakeFinished());
        Assert.Equal(RecordingEnd.Completed, finished.End);
        Assert.Equal(10, finished.Frames);
        Assert.Equal(new FileInfo(finished.Path).Length, finished.Bytes);
        Assert.Null(_recorder.Current);
        Assert.Empty(_recorder.TakeFinished());
    }

    [Fact]
    public void The_file_holds_the_shrunk_frames_their_instants_and_jpegs()
    {
        _recorder.Start(TimeSpan.FromSeconds(3), "luz-1");
        Stream(TimeSpan.FromSeconds(4));
        var finished = Assert.Single(_recorder.TakeFinished());

        using var reader = new SequenceReader(File.OpenRead(finished.Path));
        var frames = reader.ReadFrames().ToList();

        Assert.Equal(new SequenceHeader("luz-1", Start, Width / 2, Height / 2, Width, Height, 90, TimeSpan.FromSeconds(1)), reader.Header);
        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], f.Jpeg));
        Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.InRange((pair.Second.At - pair.First.At).TotalSeconds, 0.99, 1.07));
        Assert.Equal(Enumerable.Repeat((byte)100, 12), frames[0].Luma.Pixels);
    }

    [Fact]
    public void Only_one_recording_at_a_time()
    {
        _recorder.Start(TimeSpan.FromMinutes(2), "salida-1");
        _clock.Advance(TimeSpan.FromSeconds(30));

        var busy = Assert.IsType<RecordingStart.Busy>(_recorder.Start(TimeSpan.FromMinutes(1), "salida-2"));

        Assert.Equal("salida-1", busy.Recording.Name);
    }

    [Fact]
    public void Stop_ends_it_now_and_is_not_reported_again()
    {
        _recorder.Start(TimeSpan.FromMinutes(2), "salida-1");
        Stream(TimeSpan.FromSeconds(5));

        var stopped = _recorder.Stop();

        Assert.Equal(RecordingEnd.Stopped, stopped!.End);
        Assert.Equal(5, stopped.Frames);
        Assert.Null(_recorder.Current);
        Assert.Empty(_recorder.TakeFinished());
        Assert.Null(_recorder.Stop());
    }

    [Fact]
    public void It_ends_at_its_length_even_without_frames_and_creates_no_file()
    {
        _recorder.Start(TimeSpan.FromMinutes(1), "salida-1");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var finished = Assert.Single(_recorder.TakeFinished());

        Assert.Equal(0, finished.Frames);
        Assert.False(File.Exists(finished.Path));
    }

    [Fact]
    public void A_phone_short_of_space_does_not_start()
    {
        _free = 1_400_000_000;

        var refused = Assert.IsType<RecordingStart.NoSpace>(_recorder.Start(TimeSpan.FromMinutes(1), "salida-1"));

        Assert.Equal(1_400_000_000, refused.FreeBytes);
        Assert.Null(_recorder.Current);
    }

    [Fact]
    public void Running_out_of_space_stops_it_and_reports_it()
    {
        _recorder.Start(TimeSpan.FromHours(8), "casa-vacia");
        Stream(TimeSpan.FromSeconds(3));
        _free = Recorder.MinFreeBytes - 1;
        Stream(TimeSpan.FromSeconds(3));

        var finished = Assert.Single(_recorder.TakeFinished());

        Assert.Equal(RecordingEnd.LowSpace, finished.End);
        Assert.Equal(3, finished.Frames);
    }

    [Fact]
    public void Unknown_free_space_does_not_block_recording()
    {
        _free = null;

        Assert.IsType<RecordingStart.Started>(_recorder.Start(TimeSpan.FromSeconds(10), "salida-1"));
        Assert.Equal(2, Stream(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_gap_in_the_camera_starts_counting_again_without_catching_up()
    {
        _recorder.Start(TimeSpan.FromMinutes(1), "salida-1");
        Stream(TimeSpan.FromSeconds(3));
        _clock.Advance(TimeSpan.FromSeconds(20)); // camera lost

        Assert.Equal(3, Stream(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void A_write_that_fails_ends_the_recording_and_goes_to_the_log()
    {
        _recorder.Start(TimeSpan.FromMinutes(1), "salida-1");
        Assert.True(_recorder.WantsFrame());

        _recorder.Write(new byte[3], Width, Height, Width, 90, []); // a plane too short

        var finished = Assert.Single(_recorder.TakeFinished());
        Assert.Equal(RecordingEnd.Failed, finished.End);
        Assert.Contains(_log.Lines, l => l.Kind == LogKind.Error && l.Message.StartsWith("record: write failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_left_by_a_killed_recording_reads_up_to_its_last_whole_frame()
    {
        _recorder.Start(TimeSpan.FromSeconds(5), "salida-1");
        Stream(TimeSpan.FromSeconds(6));
        var path = Assert.Single(_recorder.TakeFinished()).Path;
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^3]);

        using var reader = new SequenceReader(File.OpenRead(path));

        Assert.Equal(4, reader.ReadFrames().Count());
        Assert.True(reader.Truncated);
    }

    [Fact]
    public void Start_and_end_leave_lines_in_the_log()
    {
        _recorder.Start(TimeSpan.FromSeconds(2), "salida-1");
        Stream(TimeSpan.FromSeconds(3));
        var bytes = Assert.Single(_recorder.TakeFinished()).Bytes;

        Assert.Equal(
            ["record: started salida-1 for 2 s", $"record: finished salida-1: 2 frames, {bytes} bytes, completed"],
            _log.Lines.Select(l => l.Message));
    }

    [Fact]
    public void Stopping_the_watch_interrupts_the_recording()
    {
        _recorder.Start(TimeSpan.FromMinutes(1), "salida-1");
        Stream(TimeSpan.FromSeconds(2));

        _recorder.Dispose();

        Assert.Null(_recorder.Current);
        Assert.Contains(_log.Lines, l => l.Message.EndsWith("2 frames, " + new FileInfo(Path.Combine(_directory, "20261104-153000Z-salida-1.vseq")).Length + " bytes, interrupted", StringComparison.Ordinal));
    }

    /// <summary>The camera streaming for <paramref name="length"/>; returns how many frames were recorded.</summary>
    private int Stream(TimeSpan length)
    {
        var plane = Enumerable.Repeat((byte)100, Width * Height).ToArray();
        var written = 0;
        var end = _clock.GetUtcNow() + length;
        while (_clock.GetUtcNow() < end)
        {
            if (_recorder.WantsFrame())
            {
                _recorder.Write(plane, Width, Height, Width, 90, [0xFF, 0xD8, 0xFF, 0xD9]);
                written++;
            }

            _clock.Advance(CameraFrame);
        }

        return written;
    }
}
