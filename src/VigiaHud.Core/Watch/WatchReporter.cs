namespace VigiaHud.Core.Watch;

/// <summary>What the service should do after a frame, besides counting it.</summary>
public sealed record WatchTick(CaptureReport? Report, bool Heartbeat, bool Snapshot)
{
    public static readonly WatchTick Nothing = new(null, false, false);
}

/// <summary>The frames since the last capture line in the log, and how many attempts failed.</summary>
public sealed record CaptureReport(int Frames, int Failures, TimeSpan Period);

/// <summary>
/// Keeps the count of frames and decides when the service writes a capture line, a heartbeat, or a debug snapshot.
/// Pure time arithmetic on the instants it is given, so it is tested without a camera or a clock. The first frame
/// always earns a snapshot: the quickest proof that the camera works. When the service falls behind, a due action
/// fires once and the next one is scheduled from now, never as a burst of catch-up actions.
/// </summary>
public sealed class WatchReporter
{
    private readonly WatchTimings _timings;
    private readonly DateTimeOffset _startedAt;
    private DateTimeOffset _nextReportAt;
    private DateTimeOffset _nextHeartbeatAt;
    private DateTimeOffset? _nextSnapshotAt;
    private DateTimeOffset _lastReportAt;
    private int _frames;
    private int _failures;

    public WatchReporter(WatchTimings timings, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(timings);
        timings.Validate();
        _timings = timings;
        _startedAt = startedAt;
        _lastReportAt = startedAt;
        _nextReportAt = startedAt + timings.CaptureReportInterval;
        _nextHeartbeatAt = startedAt + timings.HeartbeatInterval;
        _nextSnapshotAt = null;
    }

    public TimeSpan Uptime(DateTimeOffset now) => now - _startedAt;

    /// <summary>A frame arrived at <paramref name="now"/>.</summary>
    public WatchTick OnFrame(DateTimeOffset now)
    {
        _frames++;
        var snapshot = false;
        if (_nextSnapshotAt is null || now >= _nextSnapshotAt)
        {
            snapshot = true;
            _nextSnapshotAt = now + _timings.SnapshotInterval;
        }

        return Tick(now, snapshot);
    }

    /// <summary>A capture attempt failed at <paramref name="now"/>; the failure is counted, no snapshot is due.</summary>
    public WatchTick OnFrameFailed(DateTimeOffset now)
    {
        _failures++;
        return Tick(now, snapshot: false);
    }

    private WatchTick Tick(DateTimeOffset now, bool snapshot)
    {
        CaptureReport? report = null;
        if (now >= _nextReportAt)
        {
            report = new CaptureReport(_frames, _failures, now - _lastReportAt);
            _frames = 0;
            _failures = 0;
            _lastReportAt = now;
            _nextReportAt = now + _timings.CaptureReportInterval;
        }

        var heartbeat = false;
        if (now >= _nextHeartbeatAt)
        {
            heartbeat = true;
            _nextHeartbeatAt = now + _timings.HeartbeatInterval;
        }

        return report is null && !heartbeat && !snapshot ? WatchTick.Nothing : new WatchTick(report, heartbeat, snapshot);
    }
}
