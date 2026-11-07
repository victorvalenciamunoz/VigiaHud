using VigiaHud.Core.Motion;

namespace VigiaHud.Core.Tests.Motion;

/// <summary>
/// The detector on made-up 160x120 grey frames: a flat wall with grain, and a dark 20x20 square for somebody. Shrunk
/// to 80x60 by default, the square covers 100 of 4800 pixels, about 2 %.
/// </summary>
public class MotionDetectorTests
{
    private const int Width = 160;
    private const int Height = 120;

    [Fact]
    public void The_first_frame_has_nothing_to_compare_with() =>
        Assert.Equal(MotionVerdict.First, new MotionDetector(MotionSettings.Default).Observe(Wall(seed: 1)).Verdict);

    [Fact]
    public void Grain_on_a_still_wall_is_not_motion()
    {
        var detector = new MotionDetector(MotionSettings.Default);

        var results = Enumerable.Range(0, 10).Select(i => detector.Observe(Wall(seed: i))).ToList();

        Assert.All(results.Skip(1), r => Assert.Equal(MotionVerdict.Still, r.Verdict));
        Assert.All(results.Skip(1), r => Assert.Equal(0, r.ChangedArea));
    }

    [Fact]
    public void Somebody_moving_is_motion_from_the_second_frame_of_change()
    {
        var detector = new MotionDetector(MotionSettings.Default);
        detector.Observe(Wall(seed: 1));

        var first = detector.Observe(Wall(seed: 2, square: (20, 40)));
        var second = detector.Observe(Wall(seed: 3, square: (50, 40)));

        Assert.Equal(MotionVerdict.Changed, first.Verdict);
        Assert.Equal(MotionVerdict.Motion, second.Verdict);
        Assert.True(second.IsMotion);
        Assert.InRange(first.ChangedArea, 0.019, 0.022); // the square appears: 100 pixels
        Assert.InRange(second.ChangedArea, 0.040, 0.043); // where it is now and where it was
    }

    [Fact]
    public void One_frame_in_a_row_is_enough_when_set_so()
    {
        var detector = new MotionDetector(MotionSettings.Default with { Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        Assert.Equal(MotionVerdict.Motion, detector.Observe(Wall(seed: 2, square: (20, 40))).Verdict);
    }

    [Fact]
    public void A_change_that_stops_starts_the_count_again()
    {
        var detector = new MotionDetector(MotionSettings.Default);
        detector.Observe(Wall(seed: 1));

        Assert.Equal(MotionVerdict.Changed, detector.Observe(Wall(seed: 2, square: (20, 40))).Verdict);
        Assert.Equal(MotionVerdict.Still, detector.Observe(Wall(seed: 3, square: (20, 40))).Verdict);
        Assert.Equal(MotionVerdict.Changed, detector.Observe(Wall(seed: 4, square: (60, 40))).Verdict);
    }

    [Fact]
    public void A_change_smaller_than_the_minimum_area_is_not_motion()
    {
        var detector = new MotionDetector(MotionSettings.Default with { MinArea = 0.05, Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        var result = detector.Observe(Wall(seed: 2, square: (20, 40)));

        Assert.Equal(MotionVerdict.Still, result.Verdict);
        Assert.True(result.ChangedArea > 0);
    }

    [Fact]
    public void The_whole_room_getting_brighter_is_taken_off()
    {
        var detector = new MotionDetector(MotionSettings.Default with { Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        var result = detector.Observe(Wall(seed: 2, level: 140));

        Assert.Equal(MotionVerdict.Still, result.Verdict);
        Assert.InRange(result.BrightnessShift, 39, 41);
    }

    [Fact]
    public void Somebody_is_still_seen_while_the_room_gets_brighter()
    {
        var detector = new MotionDetector(MotionSettings.Default with { Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        var result = detector.Observe(Wall(seed: 2, level: 140, square: (20, 40)));

        Assert.Equal(MotionVerdict.Motion, result.Verdict);
        Assert.InRange(result.BrightnessShift, 39, 41);
    }

    [Fact]
    public void Nearly_everything_changing_is_the_light_and_starts_the_count_again()
    {
        var detector = new MotionDetector(MotionSettings.Default);
        detector.Observe(Wall(seed: 1));
        detector.Observe(Wall(seed: 2, square: (20, 40)));

        var flash = detector.Observe(Scrambled());
        var after = detector.Observe(Scrambled(square: (20, 40)));

        Assert.Equal(MotionVerdict.LightChange, flash.Verdict);
        Assert.Equal(MotionVerdict.Changed, after.Verdict);
    }

    [Fact]
    public void Movement_inside_an_ignored_zone_is_not_seen()
    {
        // The left half of a 4x3 grid ignored: the square at x 20-40 falls in column 0.
        var mask = new MotionMask(4, 3, [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2)]);
        var detector = new MotionDetector(MotionSettings.Default with { Consecutive = 1 }, mask);
        detector.Observe(Wall(seed: 1));

        var masked = detector.Observe(Wall(seed: 2, square: (20, 40)));
        var watched = detector.Observe(Wall(seed: 3, square: (120, 40)));

        Assert.Equal(MotionVerdict.Still, masked.Verdict);
        Assert.Equal(MotionVerdict.Motion, watched.Verdict);
        // Only half the picture is watched, so the same square is twice the share: 100 of 2400 pixels, where it is
        // now; where it was is masked.
        Assert.InRange(watched.ChangedArea, 0.040, 0.043);
    }

    [Fact]
    public void Against_the_previous_frame_somebody_standing_still_fades_out()
    {
        var detector = new MotionDetector(MotionSettings.Default);
        detector.Observe(Wall(seed: 1));
        detector.Observe(Wall(seed: 2, square: (20, 40)));

        var verdicts = Enumerable.Range(3, 5).Select(i => detector.Observe(Wall(seed: i, square: (20, 40))).Verdict).ToList();

        Assert.All(verdicts, v => Assert.Equal(MotionVerdict.Still, v));
    }

    [Fact]
    public void Against_a_background_somebody_standing_still_stays_seen_until_taken_in()
    {
        var detector = new MotionDetector(MotionSettings.Default with { Reference = MotionReference.Background, BackgroundRate = 0.05 });
        for (var i = 0; i < 5; i++)
        {
            detector.Observe(Wall(seed: i));
        }

        var verdicts = Enumerable.Range(5, 60).Select(i => detector.Observe(Wall(seed: i, square: (20, 40))).Verdict).ToList();

        // The square is 70 levels darker; at 5 % a frame the gap falls below 20 after about 25 frames.
        Assert.All(verdicts.Skip(1).Take(20), v => Assert.Equal(MotionVerdict.Motion, v));
        Assert.All(verdicts.Skip(30), v => Assert.Equal(MotionVerdict.Still, v));
    }

    [Fact]
    public void After_a_light_change_the_background_is_learnt_again()
    {
        var detector = new MotionDetector(MotionSettings.Default with { Reference = MotionReference.Background, Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        Assert.Equal(MotionVerdict.LightChange, detector.Observe(Scrambled()).Verdict);
        Assert.Equal(MotionVerdict.Still, detector.Observe(Scrambled()).Verdict);
    }

    [Fact]
    public void Reset_forgets_the_reference()
    {
        var detector = new MotionDetector(MotionSettings.Default);
        detector.Observe(Wall(seed: 1));

        detector.Reset();

        Assert.Equal(MotionVerdict.First, detector.Observe(Wall(seed: 2, square: (20, 40))).Verdict);
    }

    [Fact]
    public void Without_shrinking_the_square_is_the_same_share()
    {
        var detector = new MotionDetector(MotionSettings.Default with { ReduceFactor = 1, Consecutive = 1 });
        detector.Observe(Wall(seed: 1));

        var result = detector.Observe(Wall(seed: 2, square: (20, 40)));

        Assert.Equal(MotionVerdict.Motion, result.Verdict);
        Assert.InRange(result.ChangedArea, 0.020, 0.022); // 400 of 19 200
    }

    [Theory]
    [InlineData(0, 20, 0.01, 0.6, 2, 0.05)]
    [InlineData(2, 0, 0.01, 0.6, 2, 0.05)]
    [InlineData(2, 256, 0.01, 0.6, 2, 0.05)]
    [InlineData(2, 20, 0, 0.6, 2, 0.05)]
    [InlineData(2, 20, 0.01, 0.01, 2, 0.05)]
    [InlineData(2, 20, 0.01, 0.6, 0, 0.05)]
    [InlineData(2, 20, 0.01, 0.6, 2, 0)]
    public void Settings_out_of_range_are_refused(int reduce, int threshold, double minArea, double lightArea, int consecutive, double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionDetector(new MotionSettings
        {
            ReduceFactor = reduce,
            Threshold = threshold,
            MinArea = minArea,
            LightChangeArea = lightArea,
            Consecutive = consecutive,
            BackgroundRate = rate,
        }));

    [Fact]
    public void A_mask_maps_its_cells_onto_any_picture_size()
    {
        var mask = new MotionMask(2, 1, [(1, 0)]);

        Assert.Equal([false, false, true, true], mask.PixelsIgnored(4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MotionMask(2, 1, [(2, 0)]));
    }

    /// <summary>A wall at <paramref name="level"/> with up to ±3 levels of grain, and a dark square if asked.</summary>
    private static LumaImage Wall(int seed, int level = 100, (int X, int Y)? square = null)
    {
        var random = new Random(seed);
        var pixels = new byte[Width * Height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(level + random.Next(-3, 4));
        }

        if (square is { } at)
        {
            for (var y = at.Y; y < at.Y + 20; y++)
            {
                for (var x = at.X; x < at.X + 20; x++)
                {
                    pixels[(y * Width) + x] = (byte)(level - 70);
                }
            }
        }

        return new LumaImage(Width, Height, pixels);
    }

    /// <summary>
    /// A picture unlike the wall everywhere, as a frame gone wrong after a switch: each 2x2 block (one pixel once shrunk)
    /// a random level, always the same ones. Checkers would not do: half of them would sit exactly one shift away.
    /// </summary>
    private static LumaImage Scrambled((int X, int Y)? square = null)
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

        if (square is { } at)
        {
            for (var y = at.Y; y < at.Y + 20; y++)
            {
                for (var x = at.X; x < at.X + 20; x++)
                {
                    pixels[(y * Width) + x] = 125;
                }
            }
        }

        return new LumaImage(Width, Height, pixels);
    }
}
