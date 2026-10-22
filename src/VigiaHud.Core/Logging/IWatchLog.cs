namespace VigiaHud.Core.Logging;

/// <summary>
/// The persistent record of what the watch did: starts, stops, errors, capture ticks, heartbeats and alerts.
/// It is the judge of phase 1 (uptime measured, not assumed), so a write must never throw into the caller
/// and must reach the disk at once: Android can kill the process without notice.
/// </summary>
public interface IWatchLog
{
    void Write(LogKind kind, string message);
}
