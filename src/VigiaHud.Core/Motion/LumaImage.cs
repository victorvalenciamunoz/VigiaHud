namespace VigiaHud.Core.Motion;

/// <summary>
/// A grey picture: one brightness byte per pixel (0 black, 255 white), row after row. It comes from the camera's Y
/// plane, which a YUV frame carries apart from the colour, so getting it costs no decoding (PLAN, phase 3).
/// </summary>
public sealed class LumaImage
{
    public LumaImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"A {width}x{height} image has {width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>
    /// Shrinks a Y plane by <paramref name="factor"/> on each side, each new pixel the rounded mean of a block. The
    /// mean also smooths the sensor's grain: random jitter partly cancels out over a block, a person covering many
    /// pixels does not. Columns and rows that do not fill a whole block are dropped. <paramref name="rowStride"/> is
    /// the distance between rows in <paramref name="plane"/>, which cameras often pad; the last row may come without
    /// its padding, as Android's buffers do.
    /// </summary>
    public static LumaImage Reduce(ReadOnlySpan<byte> plane, int width, int height, int rowStride, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowStride, width);
        var outWidth = width / factor;
        var outHeight = height / factor;
        if (outWidth == 0 || outHeight == 0)
        {
            throw new ArgumentException($"A {width}x{height} plane is smaller than one {factor}x{factor} block.");
        }

        if (plane.Length < ((height - 1) * rowStride) + width)
        {
            throw new ArgumentException($"The plane has {plane.Length} bytes, too few for {width}x{height} with stride {rowStride}.", nameof(plane));
        }

        var pixels = new byte[outWidth * outHeight];
        var sums = new int[outWidth];
        var blockSize = factor * factor;
        var usedWidth = outWidth * factor;
        for (var outRow = 0; outRow < outHeight; outRow++)
        {
            Array.Clear(sums);
            for (var r = 0; r < factor; r++)
            {
                var row = plane.Slice(((outRow * factor) + r) * rowStride, usedWidth);
                for (var x = 0; x < usedWidth; x++)
                {
                    sums[x / factor] += row[x];
                }
            }

            var offset = outRow * outWidth;
            for (var outCol = 0; outCol < outWidth; outCol++)
            {
                pixels[offset + outCol] = (byte)((sums[outCol] + (blockSize / 2)) / blockSize);
            }
        }

        return new LumaImage(outWidth, outHeight, pixels);
    }
}
