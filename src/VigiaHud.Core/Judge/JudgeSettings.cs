namespace VigiaHud.Core.Judge;

/// <summary>
/// How the judge turns the detector's motion into alarms and scores them (DECISIONS 027). The defaults are the
/// provisional ones agreed with the owner for the first table (2026-11-07); the final ones come with step 3.3.
/// </summary>
public sealed record JudgeSettings
{
    public static readonly JudgeSettings Default = new();

    /// <summary>After an alarm, no other one for this long, however much keeps moving: one alarm per visit.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Motion this long before a stretch with somebody counts for that stretch, not as a false alarm: the shadow of
    /// whoever is about to come in (seen in every scene of the first batch, 2-3 s ahead).
    /// </summary>
    public TimeSpan Lead { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Motion this long after a stretch still belongs to it: the picture settling once the person is gone.</summary>
    public TimeSpan Trail { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Use one recorded frame of every this many (1: one a second, as recorded; 2: one every 2 s, as the watch).</summary>
    public int FrameStep { get; init; } = 1;

    /// <summary>A gap between frames longer than this (the camera was lost) starts the detector again.</summary>
    public TimeSpan MaxGap { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Cooldown, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(Lead, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(Trail, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FrameStep);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxGap, TimeSpan.Zero);
    }
}
