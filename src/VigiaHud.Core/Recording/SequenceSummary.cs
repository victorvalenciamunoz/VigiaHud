using System.Security.Cryptography;

namespace VigiaHud.Core.Recording;

/// <summary>
/// What there is in a recording file, for the list kept in the repo (DECISIONS 025): its header, how many frames and
/// over how long, whether it was cut short, the largest gap between frames, its size and its SHA-256, which ties each
/// result table to the exact files it was made from.
/// </summary>
public sealed record SequenceSummary(
    string FileName,
    SequenceHeader Header,
    int Frames,
    TimeSpan Length,
    TimeSpan MaxGap,
    bool Truncated,
    long Bytes,
    string Sha256)
{
    /// <summary>Reads the whole file once; a damaged one throws <see cref="InvalidDataException"/>.</summary>
    public static SequenceSummary Read(string path)
    {
        var bytes = new FileInfo(path).Length;
        string sha;
        using (var hashing = File.OpenRead(path))
        {
            sha = Convert.ToHexStringLower(SHA256.HashData(hashing));
        }

        using var reader = new SequenceReader(File.OpenRead(path));
        var frames = 0;
        DateTimeOffset? first = null;
        DateTimeOffset? previous = null;
        var maxGap = TimeSpan.Zero;
        foreach (var frame in reader.ReadFrames())
        {
            frames++;
            first ??= frame.At;
            if (previous is { } before && frame.At - before > maxGap)
            {
                maxGap = frame.At - before;
            }

            previous = frame.At;
        }

        // The last frame stands for one interval of time, as the first one does.
        var length = first is { } start ? previous!.Value - start + reader.Header.FrameInterval : TimeSpan.Zero;
        return new SequenceSummary(Path.GetFileName(path), reader.Header, frames, length, maxGap, reader.Truncated, bytes, sha);
    }
}
