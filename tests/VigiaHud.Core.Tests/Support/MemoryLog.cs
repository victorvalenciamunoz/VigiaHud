using VigiaHud.Core.Logging;

namespace VigiaHud.Core.Tests.Support;

/// <summary>Keeps the lines in memory, so a test can read what the code would have written to the log file.</summary>
public sealed class MemoryLog : IWatchLog
{
    private readonly List<(LogKind Kind, string Message)> _lines = [];
    private readonly object _gate = new();

    public IReadOnlyList<(LogKind Kind, string Message)> Lines
    {
        get { lock (_gate) { return [.. _lines]; } }
    }

    public void Write(LogKind kind, string message)
    {
        lock (_gate)
        {
            _lines.Add((kind, message));
        }
    }
}
