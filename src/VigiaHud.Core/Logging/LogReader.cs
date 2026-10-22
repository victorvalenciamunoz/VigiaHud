namespace VigiaHud.Core.Logging;

/// <summary>Reads back what <see cref="RotatingFileLog"/> wrote, oldest entry first, across the rotated files.</summary>
public static class LogReader
{
    /// <summary>Every parseable entry on disk. Torn or foreign lines are skipped, not thrown.</summary>
    public static IReadOnlyList<LogEntry> ReadAll(RotatingFileLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var entries = new List<LogEntry>();
        for (var index = options.MaxFiles - 1; index >= 1; index--)
        {
            ReadFile(options.RotatedPath(index), entries);
        }

        ReadFile(options.CurrentPath, entries);
        return entries;
    }

    /// <summary>The last <paramref name="count"/> entries, oldest first.</summary>
    public static IReadOnlyList<LogEntry> ReadLast(RotatingFileLogOptions options, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var all = ReadAll(options);
        return all.Count <= count ? all : all.Skip(all.Count - count).ToList();
    }

    private static void ReadFile(string path, List<LogEntry> entries)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (LogLine.TryParse(line, out var entry))
            {
                entries.Add(entry);
            }
        }
    }
}
