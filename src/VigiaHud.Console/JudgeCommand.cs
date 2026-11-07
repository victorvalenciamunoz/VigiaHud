using System.Globalization;
using VigiaHud.Core.Judge;
using VigiaHud.Core.Motion;
using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>
/// <c>juzgar</c>: every labelled recording through the detector with the values given (or the defaults), and the table
/// of detected, missed and false alarms for STATUS (PLAN, phase 3; DECISIONS 027). Unlabelled recordings are listed
/// and left out. Numbers only: nothing of the pictures leaves the folder.
/// </summary>
public static class JudgeCommand
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static int Run(Options options, TextWriter output)
    {
        var (motion, judge) = Settings(options);
        var (labelled, unlabelled) = Load(options);
        if (labelled.Count == 0)
        {
            output.WriteLine($"No hay grabaciones etiquetadas en {options.Folder}.");
            return 0;
        }

        output.WriteLine(Describe(motion, judge));
        output.WriteLine();
        output.WriteLine("| Grabación | Duración | Con persona | Detectadas | Perdidas | Falsas alarmas | Primer movimiento (s desde que se le ve) |");
        output.WriteLine("|---|---|---|---|---|---|---|");
        var verdicts = labelled.Select(r => Judge(r, motion, judge)).ToList();
        foreach (var v in verdicts)
        {
            var first = string.Join(", ", v.People.Select(p => p.FirstMotion is { } t ? Seconds(t) : "perdida"));
            output.WriteLine($"| {v.Name} | {SequenceLabels.Format(v.Length)} | {v.People.Count} | {v.Detected} | {v.Missed} | {v.FalseAlarms.Count} | {first} |");
        }

        var total = Total.Of(verdicts);
        output.WriteLine(string.Create(Spanish,
            $"| **Total** ({verdicts.Count}) | {SequenceLabels.Format(total.Length)} | {total.People} | {total.Detected} | {total.Missed} | {total.FalseAlarms} ({total.FalseAlarmsPerHour:0.#} por hora) | |"));
        if (unlabelled.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"Sin etiquetar, fuera del juez: {string.Join(", ", unlabelled)}.");
        }

        return 0;
    }

    /// <summary>The detector's and the judge's values from the command line, over the defaults.</summary>
    public static (MotionSettings Motion, JudgeSettings Judge) Settings(Options options)
    {
        var motion = MotionSettings.Default;
        motion = motion with
        {
            Threshold = options.Number("umbral") ?? motion.Threshold,
            MinArea = (options.Decimal("area") ?? (motion.MinArea * 100)) / 100,
            LightChangeArea = (options.Decimal("luz") ?? (motion.LightChangeArea * 100)) / 100,
            Consecutive = options.Number("seguidos") ?? motion.Consecutive,
            ReduceFactor = options.Number("reducir") ?? motion.ReduceFactor,
            Reference = options.Choice("referencia", "anterior", "fondo") == "fondo" ? MotionReference.Background : motion.Reference,
            BackgroundRate = (options.Decimal("aprendizaje") ?? (motion.BackgroundRate * 100)) / 100,
        };
        var judge = JudgeSettings.Default;
        judge = judge with
        {
            FrameStep = options.Number("cada") ?? judge.FrameStep,
            Cooldown = options.Number("cooldown") is { } cooldown ? TimeSpan.FromSeconds(cooldown) : judge.Cooldown,
            Lead = options.Number("adelanto") is { } lead ? TimeSpan.FromSeconds(lead) : judge.Lead,
        };
        try
        {
            motion.Validate();
            judge.Validate();
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new UsageException($"Valores que no valen: {error.ParamName} = {error.ActualValue}.");
        }

        return (motion, judge);
    }

    /// <summary>The recordings with labels, and the names of those without.</summary>
    public static (IReadOnlyList<(string Path, SequenceLabels Labels)> Labelled, IReadOnlyList<string> Unlabelled) Load(Options options)
    {
        var labelled = new List<(string, SequenceLabels)>();
        var unlabelled = new List<string>();
        foreach (var path in options.Recordings())
        {
            SequenceLabels? labels;
            try
            {
                labels = SequenceLabels.ReadFor(path);
            }
            catch (FormatException error)
            {
                throw new UsageException($"{Path.GetFileName(SequenceLabels.PathFor(path))}: {error.Message}");
            }

            if (labels is null)
            {
                unlabelled.Add(Path.GetFileNameWithoutExtension(path));
            }
            else
            {
                labelled.Add((path, labels));
            }
        }

        return (labelled, unlabelled);
    }

    public static RecordingVerdict Judge((string Path, SequenceLabels Labels) recording, MotionSettings motion, JudgeSettings judge)
    {
        using var reader = new SequenceReader(File.OpenRead(recording.Path));
        return MotionJudge.Run(reader.Header, reader.ReadFrames(), recording.Labels, motion, judge);
    }

    public static string Describe(MotionSettings motion, JudgeSettings judge) => string.Create(Spanish,
        $"Valores: umbral {motion.Threshold} · área {motion.MinArea * 100:0.##} % · luz {motion.LightChangeArea * 100:0.##} % · " +
        $"{motion.Consecutive} fotos seguidas · {(motion.Reference == MotionReference.Previous ? "contra la foto anterior" : $"contra el fondo ({motion.BackgroundRate * 100:0.##} % por foto)")} · " +
        $"encoger ×{motion.ReduceFactor} · una foto cada {judge.FrameStep} s · cooldown {judge.Cooldown.TotalSeconds:0} s · adelanto {judge.Lead.TotalSeconds:0} s");

    /// <summary>"−3", "0", "2": seconds, with a proper minus sign for motion that came before the person was seen.</summary>
    private static string Seconds(TimeSpan t) => ((int)t.TotalSeconds).ToString(Spanish).Replace('-', '−');

    /// <summary>The sums over a set of recordings.</summary>
    public sealed record Total(TimeSpan Length, int People, int Detected, int Missed, int FalseAlarms)
    {
        public double FalseAlarmsPerHour => Length > TimeSpan.Zero ? FalseAlarms / Length.TotalHours : 0;

        public static Total Of(IReadOnlyCollection<RecordingVerdict> verdicts) => new(
            TimeSpan.FromTicks(verdicts.Sum(v => v.Length.Ticks)),
            verdicts.Sum(v => v.People.Count),
            verdicts.Sum(v => v.Detected),
            verdicts.Sum(v => v.Missed),
            verdicts.Sum(v => v.FalseAlarms.Count));
    }
}
