using System.Globalization;
using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>
/// <c>info</c>: one Markdown row per recording, ready for <c>docs/secuencias.md</c> (DECISIONS 025). Names, numbers
/// and hashes only, never a picture. A damaged file or a labels file that does not read gets its row with the reason,
/// and the exit code says something needs looking at.
/// </summary>
public static class InfoCommand
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static int Run(Options options, TextWriter output)
    {
        var recordings = options.Recordings();
        if (recordings.Count == 0)
        {
            output.WriteLine($"No hay grabaciones en {options.Folder}.");
            return 0;
        }

        output.WriteLine("| Grabación | Inicio (UTC) | Duración | Fotos | Etiquetas | Tamaño | SHA-256 |");
        output.WriteLine("|---|---|---|---|---|---|---|");
        var problems = 0;
        foreach (var path in recordings)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            SequenceSummary summary;
            try
            {
                summary = SequenceSummary.Read(path);
            }
            catch (InvalidDataException error)
            {
                problems++;
                output.WriteLine($"| {name} | | | | dañada: {error.Message} | | |");
                continue;
            }

            string labels;
            try
            {
                labels = Describe(SequenceLabels.ReadFor(path));
            }
            catch (FormatException error)
            {
                problems++;
                labels = $"etiquetas mal: {error.Message}";
            }

            output.WriteLine(string.Create(Spanish,
                $"| {name} | {summary.Header.StartedAt.UtcDateTime:yyyy-MM-dd HH:mm} | {SequenceLabels.Format(summary.Length)} | {Frames(summary)} | {labels} | {summary.Bytes / 1e6:0.0} MB | `{summary.Sha256}` |"));
        }

        return problems == 0 ? 0 : 1;
    }

    private static string Describe(SequenceLabels? labels) => labels switch
    {
        null => "sin etiquetar",
        { AnyPerson: false } => "sin nadie",
        _ => string.Join(", ", labels.People.Select(p => $"{SequenceLabels.Format(p.Start)}-{SequenceLabels.Format(p.End)}")),
    };

    /// <summary>The count, and what is odd about it: cut short, or a gap of more than three frames (camera lost).</summary>
    private static string Frames(SequenceSummary summary)
    {
        var notes = new List<string>();
        if (summary.Truncated)
        {
            notes.Add("cortada");
        }

        if (summary.MaxGap > summary.Header.FrameInterval * 3)
        {
            notes.Add(string.Create(Spanish, $"hueco de {summary.MaxGap.TotalSeconds:0} s"));
        }

        return notes.Count == 0
            ? summary.Frames.ToString(Spanish)
            : $"{summary.Frames.ToString(Spanish)} ({string.Join(", ", notes)})";
    }
}
