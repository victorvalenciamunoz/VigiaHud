using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>
/// <c>hoja</c>: a page with the recording's photos, next to the recording and never in the repo (they show the inside
/// of a home), for the owner to look at and label by tapping each photo with somebody in (DECISIONS 025). At most <see cref="MaxPhotos"/> photos, evenly spread, so a night still opens; a
/// stretch of it can be looked at photo by photo with <c>--desde</c> and <c>--hasta</c>.
/// </summary>
public static class SheetCommand
{
    public const int MaxPhotos = 600;

    public static int Run(Options options, TextWriter output)
    {
        var path = options.Recording();
        var from = options.Time("desde") ?? TimeSpan.Zero;
        var to = options.Time("hasta") ?? TimeSpan.MaxValue;
        var every = options.Number("cada");
        if (to < from)
        {
            throw new UsageException("--hasta va antes de --desde.");
        }

        SequenceLabels? labels;
        try
        {
            labels = SequenceLabels.ReadFor(path);
        }
        catch (FormatException error)
        {
            throw new UsageException($"{Path.GetFileName(SequenceLabels.PathFor(path))}: {error.Message}");
        }

        // First pass: how many photos fall in the window, to spread at most MaxPhotos over it.
        var inWindow = 0;
        using (var counting = new SequenceReader(File.OpenRead(path)))
        {
            inWindow = counting.ReadFrames().Count(f => InWindow(f.At - counting.Header.StartedAt, from, to));
        }

        if (inWindow == 0)
        {
            throw new UsageException("No hay fotos en ese rato.");
        }

        var stride = every is null ? (int)Math.Ceiling(inWindow / (double)MaxPhotos) : 0;
        var photos = new List<SheetPage.Photo>();
        SequenceHeader header;
        using (var reader = new SequenceReader(File.OpenRead(path)))
        {
            header = reader.Header;
            var index = 0;
            TimeSpan? next = null;
            foreach (var frame in reader.ReadFrames())
            {
                var offset = frame.At - header.StartedAt;
                if (!InWindow(offset, from, to))
                {
                    continue;
                }

                var keep = every is not null
                    ? next is null || offset >= next
                    : index % stride == 0;
                index++;
                if (keep)
                {
                    photos.Add(new SheetPage.Photo(offset, frame.Jpeg));
                    next = offset + TimeSpan.FromSeconds(every ?? 1);
                }
            }
        }

        var spacing = every ?? (int)Math.Round(stride * header.FrameInterval.TotalSeconds);
        var sheet = Path.ChangeExtension(path, null) + (from > TimeSpan.Zero || to != TimeSpan.MaxValue
            ? $".hoja-{SequenceLabels.Format(from).Replace(':', '.')}.html"
            : ".hoja.html");
        File.WriteAllText(sheet, SheetPage.Build(header, photos, spacing, labels, Path.GetFileName(SequenceLabels.PathFor(path))));
        output.WriteLine($"{photos.Count} fotos de {inWindow}, una cada {spacing} s: {sheet}");
        output.WriteLine($"Ábrela en el navegador; las etiquetas van en {SequenceLabels.PathFor(path)}.");
        return 0;
    }

    private static bool InWindow(TimeSpan offset, TimeSpan from, TimeSpan to) =>
        offset >= from && (to == TimeSpan.MaxValue || offset < to + TimeSpan.FromSeconds(1));
}
