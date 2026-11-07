namespace VigiaHud.Core.Motion;

/// <summary>What each frame is compared with.</summary>
public enum MotionReference
{
    /// <summary>The previous frame: "what changed in the last second". A person standing still fades out.</summary>
    Previous,

    /// <summary>
    /// A background that learns slowly (<see cref="MotionSettings.BackgroundRate"/>): "what was not here before". A
    /// person standing still stays visible until the background takes them in.
    /// </summary>
    Background,
}

/// <summary>
/// The knobs of the motion detector (PLAN, phase 3; DECISIONS 026). The defaults are a starting point for the judge,
/// not chosen values: the owner picks them in step 3.3 from the tables of the recordings.
/// </summary>
public sealed record MotionSettings
{
    public static readonly MotionSettings Default = new();

    /// <summary>Shrinks each grey frame again before comparing (160x120 to 80x60): less grain, fewer numbers.</summary>
    public int ReduceFactor { get; init; } = 2;

    /// <summary>A pixel counts as changed when it moved more than this many levels (of 255), the light change taken off.</summary>
    public int Threshold { get; init; } = 20;

    /// <summary>The share of the watched picture that has to change for it to be motion.</summary>
    public double MinArea { get; init; } = 0.01;

    /// <summary>
    /// A change this large, even after taking off the overall brightness shift, is the light or the camera's exposure
    /// (a frame gone very dark or very bright after a switch, seen in luz-techo), not somebody: it is ignored.
    /// </summary>
    public double LightChangeArea { get; init; } = 0.6;

    /// <summary>How many frames in a row must show change before it is motion: one odd frame is not somebody.</summary>
    public int Consecutive { get; init; } = 2;

    public MotionReference Reference { get; init; } = MotionReference.Previous;

    /// <summary>With <see cref="MotionReference.Background"/>: how much of each new frame goes into the background.</summary>
    public double BackgroundRate { get; init; } = 0.05;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReduceFactor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Threshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Threshold, 255);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Consecutive);
        if (MinArea is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinArea), MinArea, "A share between 0 and 1.");
        }

        if (LightChangeArea <= MinArea || LightChangeArea > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(LightChangeArea), LightChangeArea, "A share above MinArea and at most 1.");
        }

        if (BackgroundRate is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BackgroundRate), BackgroundRate, "A share between 0 and 1.");
        }
    }
}
