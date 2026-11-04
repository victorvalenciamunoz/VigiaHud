using System.Security.Cryptography;
using VigiaHud.Core.Motion;
using VigiaHud.Core.Recording;

namespace VigiaHud.Core.Tests.Recording;

public sealed class SequenceSummaryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 10, 3, 19, TimeSpan.Zero);
    private static readonly SequenceHeader Header = new("salida-1", Start, 2, 2, 4, 4, 90, TimeSpan.FromSeconds(1));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));

    public SequenceSummaryTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Says_frames_length_size_and_hash()
    {
        var path = Write("a.vseq", [0, 1, 2, 3, 4]);

        var summary = SequenceSummary.Read(path);

        Assert.Equal("a.vseq", summary.FileName);
        Assert.Equal(Header, summary.Header);
        Assert.Equal(5, summary.Frames);
        Assert.Equal(TimeSpan.FromSeconds(5), summary.Length);
        Assert.Equal(TimeSpan.FromSeconds(1), summary.MaxGap);
        Assert.False(summary.Truncated);
        Assert.Equal(new FileInfo(path).Length, summary.Bytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), summary.Sha256);
    }

    [Fact]
    public void A_gap_in_the_frames_shows_as_the_largest_gap()
    {
        var summary = SequenceSummary.Read(Write("a.vseq", [0, 1, 30, 31]));

        Assert.Equal(TimeSpan.FromSeconds(29), summary.MaxGap);
        Assert.Equal(TimeSpan.FromSeconds(32), summary.Length);
    }

    [Fact]
    public void A_cut_file_says_so()
    {
        var path = Write("a.vseq", [0, 1, 2]);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^2]);

        var summary = SequenceSummary.Read(path);

        Assert.True(summary.Truncated);
        Assert.Equal(2, summary.Frames);
    }

    [Fact]
    public void A_recording_without_frames_lasts_nothing()
    {
        var summary = SequenceSummary.Read(Write("a.vseq", []));

        Assert.Equal(0, summary.Frames);
        Assert.Equal(TimeSpan.Zero, summary.Length);
    }

    private string Write(string name, int[] seconds)
    {
        var path = Path.Combine(_directory, name);
        using var writer = new SequenceWriter(File.Create(path), Header);
        foreach (var second in seconds)
        {
            writer.Write(Start.AddSeconds(second), new LumaImage(2, 2, [1, 2, 3, (byte)second]), [0xFF, 0xD8, 0xFF, 0xD9]);
        }

        return path;
    }
}
