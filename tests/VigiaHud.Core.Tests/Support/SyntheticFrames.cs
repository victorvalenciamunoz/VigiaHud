using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Tests.Support;

/// <summary>
/// Made-up 160x120 grey frames, the size of a recording: a flat wall with grain, a dark 20x20 square for somebody,
/// and a scrambled picture for a frame gone wrong after a light switch. Shrunk to 80x60, the square is about 2 %.
/// </summary>
public static class SyntheticFrames
{
    public const int Width = 160;
    public const int Height = 120;

    /// <summary>A wall at <paramref name="level"/> with up to ±3 levels of grain, and a dark square if asked.</summary>
    public static LumaImage Wall(int seed, int level = 100, (int X, int Y)? square = null)
    {
        var random = new Random(seed);
        var pixels = new byte[Width * Height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(level + random.Next(-3, 4));
        }

        Square(pixels, square, (byte)(level - 70));
        return new LumaImage(Width, Height, pixels);
    }

    /// <summary>
    /// A picture unlike the wall everywhere, as a frame gone wrong after a switch: each 2x2 block (one pixel once shrunk)
    /// a random level, always the same ones. Checkers would not do: half of them would sit exactly one shift away.
    /// </summary>
    public static LumaImage Scrambled((int X, int Y)? square = null)
    {
        var random = new Random(42);
        var levels = Enumerable.Range(0, (Width / 2) * (Height / 2)).Select(_ => (byte)random.Next(256)).ToArray();
        var pixels = new byte[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                pixels[(y * Width) + x] = levels[((y / 2) * (Width / 2)) + (x / 2)];
            }
        }

        Square(pixels, square, 125);
        return new LumaImage(Width, Height, pixels);
    }

    private static void Square(byte[] pixels, (int X, int Y)? square, byte level)
    {
        if (square is not { } at)
        {
            return;
        }

        for (var y = at.Y; y < at.Y + 20; y++)
        {
            for (var x = at.X; x < at.X + 20; x++)
            {
                pixels[(y * Width) + x] = level;
            }
        }
    }
}
