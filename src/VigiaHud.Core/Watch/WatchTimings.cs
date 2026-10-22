namespace VigiaHud.Core.Watch;

/// <summary>
/// How often the watch does each thing. The defaults are phase 1's: a frame every 2 s, a capture line in the log
/// every minute (the judge looks for gaps longer than that), a heartbeat and a debug snapshot every 10 minutes.
/// </summary>
public sealed record WatchTimings
{
    public static readonly WatchTimings Default = new();

    public TimeSpan CaptureInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CaptureReportInterval { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromMinutes(10);

    public void Validate()
    {
        foreach (var (name, value) in new[]
                 {
                     (nameof(CaptureInterval), CaptureInterval),
                     (nameof(CaptureReportInterval), CaptureReportInterval),
                     (nameof(HeartbeatInterval), HeartbeatInterval),
                     (nameof(SnapshotInterval), SnapshotInterval),
                 })
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name, value, "Every interval must be positive.");
            }
        }
    }
}
