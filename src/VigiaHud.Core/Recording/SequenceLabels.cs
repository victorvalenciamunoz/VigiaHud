using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VigiaHud.Core.Recording;

/// <summary>
/// A stretch of a recording with somebody in it, as seconds from the start of the recording. Both ends name frames,
/// and both are in: <c>persona 00:12-00:31</c> covers every frame from second 12 up to, not including, second 32.
/// </summary>
public sealed record PersonInterval(TimeSpan Start, TimeSpan End)
{
    public bool Contains(TimeSpan offset) => offset >= Start && offset < End + TimeSpan.FromSeconds(1);

    /// <summary>The line in the labels file: <c>persona 00:12-00:31</c>, or with hours past the first one.</summary>
    public override string ToString() => $"persona {SequenceLabels.Format(Start)}-{SequenceLabels.Format(End)}";
}

/// <summary>
/// What a person saw in a recording, from the text file next to it with the same name and <c>.txt</c> (DECISIONS 025):
/// one <c>persona desde-hasta</c> line per stretch with somebody in, <c>#</c> for comments. A recording without the
/// file is not labelled and the judge leaves it out; a file without <c>persona</c> lines says nobody was there.
/// </summary>
public sealed partial class SequenceLabels
{
    public const string Extension = ".txt";

    private SequenceLabels(IReadOnlyList<PersonInterval> people) => People = people;

    public IReadOnlyList<PersonInterval> People { get; }

    public bool AnyPerson => People.Count > 0;

    /// <summary>Whether somebody was in the frame <paramref name="offset"/> after the recording started.</summary>
    public bool HasPersonAt(TimeSpan offset) => People.Any(p => p.Contains(offset));

    /// <summary>Reads a labels file; a line that does not read says which one and why, in Spanish, for the owner.</summary>
    /// <exception cref="FormatException">A line is not a comment, blank, or a valid <c>persona</c> line.</exception>
    public static SequenceLabels Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var people = new List<PersonInterval>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var match = PersonLine().Match(line);
            if (!match.Success)
            {
                throw new FormatException($"Línea {i + 1}: «{line}» no es «persona mm:ss-mm:ss» ni un comentario con #.");
            }

            var start = ParseTime(match.Groups["start"].Value, i + 1);
            var end = ParseTime(match.Groups["end"].Value, i + 1);
            if (end < start)
            {
                throw new FormatException($"Línea {i + 1}: el tramo acaba ({Format(end)}) antes de empezar ({Format(start)}).");
            }

            people.Add(new PersonInterval(start, end));
        }

        return new SequenceLabels([.. people.OrderBy(p => p.Start)]);
    }

    /// <summary>The labels file of a recording, or null if it has none.</summary>
    public static SequenceLabels? ReadFor(string recordingPath)
    {
        var path = PathFor(recordingPath);
        return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : null;
    }

    public static string PathFor(string recordingPath) => Path.ChangeExtension(recordingPath, Extension);

    /// <summary>"00:12", "59:59", "1:00:00": minutes and seconds, with hours once there are any.</summary>
    public static string Format(TimeSpan offset) => offset.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)offset.TotalHours}:{offset.Minutes:00}:{offset.Seconds:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{offset.Minutes:00}:{offset.Seconds:00}");

    /// <summary>"00:12", "75:10" or "1:15:10" as an offset from the start of the recording.</summary>
    /// <exception cref="FormatException">Not one of those, or seconds (or minutes, with hours) past 59.</exception>
    public static TimeSpan ParseOffset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Offset().IsMatch(text))
        {
            throw new FormatException($"«{text}» no es mm:ss ni h:mm:ss.");
        }

        var parts = text.Split(':').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var (hours, minutes, seconds) = parts.Length == 3 ? (parts[0], parts[1], parts[2]) : (0, parts[0], parts[1]);
        if (seconds > 59 || (parts.Length == 3 && minutes > 59))
        {
            throw new FormatException($"«{text}» no es una hora válida.");
        }

        return new TimeSpan(hours, minutes, seconds);
    }

    private static TimeSpan ParseTime(string text, int line)
    {
        try
        {
            return ParseOffset(text);
        }
        catch (FormatException error)
        {
            throw new FormatException($"Línea {line}: {error.Message}", error);
        }
    }

    private const string OffsetPattern = @"\d{1,3}(:\d{1,2}){1,2}";

    [GeneratedRegex($"^persona\\s+(?<start>{OffsetPattern})\\s*-\\s*(?<end>{OffsetPattern})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonLine();

    [GeneratedRegex($"^{OffsetPattern}$", RegexOptions.CultureInvariant)]
    private static partial Regex Offset();
}
