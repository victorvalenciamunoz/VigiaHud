namespace VigiaHud.Core.Recording;

/// <summary>How a recording ended.</summary>
public enum RecordingEnd
{
    /// <summary>It ran its whole length.</summary>
    Completed,

    /// <summary>The owner cut it with <c>/grabar parar</c>.</summary>
    Stopped,

    /// <summary>The phone got down to <see cref="Recorder.MinFreeBytes"/> free.</summary>
    LowSpace,

    /// <summary>Writing failed; the error is in the log.</summary>
    Failed,

    /// <summary>The watch stopped while recording.</summary>
    Interrupted,
}

/// <summary>A recording, running (<see cref="End"/> null) or finished.</summary>
public sealed record RecordingState(
    string Name,
    string Path,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int Frames,
    long Bytes,
    RecordingEnd? End)
{
    public DateTimeOffset EndsAt => StartedAt + Duration;
}

/// <summary>The answer to a request to start recording.</summary>
public abstract record RecordingStart
{
    private RecordingStart()
    {
    }

    public sealed record Started(RecordingState Recording) : RecordingStart;

    /// <summary>One is already running; only one at a time.</summary>
    public sealed record Busy(RecordingState Recording) : RecordingStart;

    /// <summary>The phone has less than <see cref="Recorder.MinFreeBytes"/> free.</summary>
    public sealed record NoSpace(long FreeBytes) : RecordingStart;
}
