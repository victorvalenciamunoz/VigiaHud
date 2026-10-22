namespace VigiaHud.Core.Logging;

/// <summary>What a log entry is about. The judge of phase 1 counts starts, stops, errors and capture ticks.</summary>
public enum LogKind
{
    /// <summary>The app or the watch service started.</summary>
    Start,
    /// <summary>The app or the watch service stopped, on purpose or not.</summary>
    Stop,
    /// <summary>Something failed. The message says what.</summary>
    Error,
    /// <summary>A capture tick: how many frames since the last tick.</summary>
    Capture,
    /// <summary>The periodic sign of life: battery, temperature, uptime.</summary>
    Heartbeat,
    /// <summary>A warning sent to the owner (a person, no power, low battery).</summary>
    Alert,
    /// <summary>Anything else worth keeping.</summary>
    Info,
}
