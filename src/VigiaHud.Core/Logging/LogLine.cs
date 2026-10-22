using System.Globalization;
using System.Text;

namespace VigiaHud.Core.Logging;

/// <summary>
/// The text form of a <see cref="LogEntry"/>: one line, three tab-separated fields (UTC instant, kind, message).
/// Tabs and line breaks inside the message are escaped so a line is always one entry: the judge scripts split on
/// line breaks and tabs and nothing else.
/// </summary>
public static class LogLine
{
    private const char Separator = '\t';
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Concat(
            entry.At.ToString(InstantFormat, CultureInfo.InvariantCulture),
            Separator,
            entry.Kind.ToString().ToLowerInvariant(),
            Separator,
            Escape(entry.Message));
    }

    /// <summary>Parses a line written by <see cref="Format"/>; false for anything else (a torn last line, a blank).</summary>
    public static bool TryParse(string? line, out LogEntry entry)
    {
        entry = null!;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var parts = line.Split(Separator, 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(parts[0], InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            return false;
        }

        if (!Enum.TryParse<LogKind>(parts[1], ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return false;
        }

        entry = new LogEntry(at, kind, Unescape(parts[2]));
        return true;
    }

    private static string Escape(string message)
    {
        if (message.AsSpan().IndexOfAny("\\\t\r\n") < 0)
        {
            return message;
        }

        var builder = new StringBuilder(message.Length + 8);
        foreach (var c in message)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\t': builder.Append("\\t"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            i++;
            builder.Append(text[i] switch
            {
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                var other => other,
            });
        }

        return builder.ToString();
    }
}
