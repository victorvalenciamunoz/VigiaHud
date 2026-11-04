using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Tests.Motion;

public class LumaImageTests
{
    [Fact]
    public void Each_new_pixel_is_the_rounded_mean_of_its_block()
    {
        byte[] plane =
        [
            10, 20, 100, 100,
            30, 41, 100, 102,
            0, 0, 255, 255,
            0, 1, 255, 254,
        ];

        var image = LumaImage.Reduce(plane, 4, 4, 4, 2);

        // 101/4 = 25.25 → 25; 402/4 = 100.5 → 101; 1/4 → 0; 1019/4 = 254.75 → 255.
        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal([25, 101, 0, 255], image.Pixels);
    }

    [Fact]
    public void The_padding_at_the_end_of_each_row_is_left_out()
    {
        // Rows 3 bytes apart, the third one (99) padding; the last row comes without it, as Android's buffers do.
        byte[] plane =
        [
            8, 8, 99,
            8, 8, 99,
            4, 4, 99,
            4, 4,
        ];

        var image = LumaImage.Reduce(plane, 2, 4, 3, 2);

        Assert.Equal([8, 4], image.Pixels);
    }

    [Fact]
    public void Columns_and_rows_that_do_not_fill_a_block_are_dropped()
    {
        byte[] plane =
        [
            2, 2, 200,
            2, 2, 200,
            200, 200, 200,
        ];

        var image = LumaImage.Reduce(plane, 3, 3, 3, 2);

        Assert.Equal((1, 1), (image.Width, image.Height));
        Assert.Equal([2], image.Pixels);
    }

    [Fact]
    public void A_camera_frame_halves_to_the_recording_size()
    {
        var plane = Enumerable.Range(0, 320 * 240).Select(i => (byte)(i % 251)).ToArray();

        var image = LumaImage.Reduce(plane, 320, 240, 320, 2);

        Assert.Equal((160, 120), (image.Width, image.Height));
        Assert.Equal((byte)((0 + 1 + 320 % 251 + 321 % 251 + 2) / 4), image.Pixels[0]);
    }

    [Fact]
    public void Factor_one_copies_without_the_padding() =>
        Assert.Equal([1, 2, 3, 4], LumaImage.Reduce([1, 2, 0, 3, 4], 2, 2, 3, 1).Pixels);

    [Fact]
    public void A_plane_too_short_for_its_size_is_refused() =>
        Assert.Throws<ArgumentException>(() => LumaImage.Reduce(new byte[7], 4, 2, 4, 2));

    [Fact]
    public void A_plane_smaller_than_one_block_is_refused() =>
        Assert.Throws<ArgumentException>(() => LumaImage.Reduce(new byte[4], 1, 4, 1, 2));

    [Fact]
    public void An_image_must_have_as_many_pixels_as_its_size_says() =>
        Assert.Throws<ArgumentException>(() => new LumaImage(2, 2, new byte[3]));
}
