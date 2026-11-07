namespace VigiaHud.Core.Motion;

/// <summary>
/// Zones the detector does not watch (PLAN, phase 3: windows, curtains), as cells of a grid laid over the picture in
/// the camera's own orientation. Whether masks are worth having is decided with the judge's numbers (STATUS); the
/// editor to draw them on the phone is step 5. The grid does not depend on the picture size, so the same mask applies
/// to the 160x120 frames and to the 80x60 ones the detector compares.
/// </summary>
public sealed class MotionMask
{
    public static readonly MotionMask None = new(1, 1, []);

    private readonly bool[] _ignored;

    public MotionMask(int columns, int rows, IEnumerable<(int Column, int Row)> ignored)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentNullException.ThrowIfNull(ignored);
        Columns = columns;
        Rows = rows;
        _ignored = new bool[columns * rows];
        foreach (var (column, row) in ignored)
        {
            if (column < 0 || column >= columns || row < 0 || row >= rows)
            {
                throw new ArgumentOutOfRangeException(nameof(ignored), $"Cell ({column}, {row}) is outside a {columns}x{rows} grid.");
            }

            _ignored[(row * columns) + column] = true;
        }
    }

    public int Columns { get; }

    public int Rows { get; }

    public bool IsIgnored(int column, int row) => _ignored[(row * Columns) + column];

    /// <summary>Which pixels of a <paramref name="width"/> x <paramref name="height"/> picture fall in an ignored cell.</summary>
    public bool[] PixelsIgnored(int width, int height)
    {
        var pixels = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * Rows / height;
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = IsIgnored(x * Columns / width, row);
            }
        }

        return pixels;
    }
}
