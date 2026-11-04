using VigiaHud.Core.Motion;
using VigiaHud.Core.Recording;

namespace VigiaHud.Core.Tests.Recording;

public class SequenceFileTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 15, 30, 0, TimeSpan.Zero);
    private static readonly SequenceHeader Header = new("salida-1", Start, 4, 3, 8, 6, 90, TimeSpan.FromSeconds(1));

    [Fact]
    public void Frames_come_back_exactly_as_written()
    {
        var bytes = Write(3);

        using var reader = new SequenceReader(new MemoryStream(bytes));
        var frames = reader.ReadFrames().ToList();

        Assert.Equal(Header, reader.Header);
        Assert.Equal(3, frames.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Start.AddSeconds(i), frames[i].At);
            Assert.Equal(Luma(i).Pixels, frames[i].Luma.Pixels);
            Assert.Equal(Jpeg(i), frames[i].Jpeg);
        }

        Assert.False(reader.Truncated);
    }

    [Fact]
    public void A_recording_without_frames_has_its_header_and_nothing_else()
    {
        using var reader = new SequenceReader(new MemoryStream(Write(0)));

        Assert.Equal("salida-1", reader.Header.Name);
        Assert.Empty(reader.ReadFrames());
        Assert.False(reader.Truncated);
    }

    [Fact]
    public void A_file_cut_anywhere_in_the_last_frame_reads_up_to_the_frame_before()
    {
        var two = Write(2).Length;
        var three = Write(3);

        for (var cut = two + 1; cut < three.Length; cut++)
        {
            using var reader = new SequenceReader(new MemoryStream(three[..cut]));

            Assert.Equal(2, reader.ReadFrames().Count());
            Assert.True(reader.Truncated, $"cut at {cut} of {three.Length}");
        }
    }

    [Fact]
    public void A_file_cut_inside_its_header_is_not_a_recording()
    {
        var bytes = Write(0);

        Assert.Throws<InvalidDataException>(() => new SequenceReader(new MemoryStream(bytes[..(bytes.Length - 1)])));
    }

    [Fact]
    public void Another_file_is_not_a_recording() =>
        Assert.Throws<InvalidDataException>(() => new SequenceReader(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0])));

    [Fact]
    public void A_damaged_frame_says_which_one()
    {
        var bytes = Write(2);
        bytes[^(Jpeg(1).Length + sizeof(int))]++; // the last frame's JPEG length no longer fits its record

        using var reader = new SequenceReader(new MemoryStream(bytes));
        var error = Assert.Throws<InvalidDataException>(() => reader.ReadFrames().ToList());

        Assert.StartsWith("Frame 1:", error.Message);
    }

    [Fact]
    public void A_frame_of_another_size_is_refused()
    {
        using var writer = new SequenceWriter(new MemoryStream(), Header);

        Assert.Throws<ArgumentException>(() => writer.Write(Start, new LumaImage(2, 2, new byte[4]), []));
    }

    [Fact]
    public void The_writer_counts_frames_and_bytes()
    {
        var stream = new MemoryStream();
        using var writer = new SequenceWriter(stream, Header);
        writer.Write(Start, Luma(0), Jpeg(0));

        Assert.Equal(1, writer.FramesWritten);
        Assert.Equal(stream.Length, writer.BytesWritten);
    }

    [Fact]
    public void A_flat_grey_picture_shrinks_a_lot()
    {
        var header = Header with { Width = 160, Height = 120 };
        var stream = new MemoryStream();
        using (var writer = new SequenceWriter(stream, header))
        {
            writer.Write(Start, new LumaImage(160, 120, Enumerable.Repeat((byte)120, 160 * 120).ToArray()), []);
        }

        Assert.True(stream.ToArray().Length < 1000, $"{stream.ToArray().Length} bytes");
    }

    private static byte[] Write(int frames)
    {
        var stream = new MemoryStream();
        using (var writer = new SequenceWriter(stream, Header))
        {
            for (var i = 0; i < frames; i++)
            {
                writer.Write(Start.AddSeconds(i), Luma(i), Jpeg(i));
            }
        }

        return stream.ToArray();
    }

    private static LumaImage Luma(int i) => new(4, 3, [.. Enumerable.Range(0, 12).Select(p => (byte)((p * 17) + (i * 5)))]);

    private static byte[] Jpeg(int i) => [0xFF, 0xD8, (byte)i, 0xFF, 0xD9];
}
