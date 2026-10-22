namespace VigiaHud.Core.Logging;

/// <summary>One line of the log. <see cref="At"/> is always UTC; local time is for showing only.</summary>
public sealed record LogEntry
{
    public LogEntry(DateTimeOffset at, LogKind kind, string message)
    {
        At = at.ToUniversalTime();
        Kind = kind;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public DateTimeOffset At { get; }

    public LogKind Kind { get; }

    public string Message { get; }
}
