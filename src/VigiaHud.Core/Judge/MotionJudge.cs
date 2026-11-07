using VigiaHud.Core.Motion;
using VigiaHud.Core.Recording;

namespace VigiaHud.Core.Judge;

/// <summary>One labelled stretch with somebody, and whether the detector saw it.</summary>
/// <param name="FirstMotion">Seconds from the start of the stretch to the first motion that counts for it; negative
/// when it came before (a shadow); null if none came.</param>
public sealed record PersonOutcome(PersonInterval Person, TimeSpan? FirstMotion)
{
    public bool Detected => FirstMotion is not null;
}

/// <summary>The judge's verdict on one recording: every stretch with somebody, every alarm, and which were false.</summary>
public sealed record RecordingVerdict(
    string Name,
    TimeSpan Length,
    int FramesUsed,
    IReadOnlyList<PersonOutcome> People,
    IReadOnlyList<TimeSpan> Alarms,
    IReadOnlyList<TimeSpan> FalseAlarms)
{
    public int Detected => People.Count(p => p.Detected);

    public int Missed => People.Count(p => !p.Detected);
}

/// <summary>
/// Plays a recording through the <see cref="MotionDetector"/> as the phone would and scores it against its labels
/// (PLAN, phase 3; DECISIONS 027):
/// <list type="bullet">
/// <item>An <b>alarm</b> is the first motion after <see cref="JudgeSettings.Cooldown"/> without one.</item>
/// <item>A stretch with somebody is <b>detected</b> if there is motion from <see cref="JudgeSettings.Lead"/> before it
/// to <see cref="JudgeSettings.Trail"/> after it, whether or not the cooldown let an alarm out: the cooldown is about
/// not flooding the owner, not about seeing.</item>
/// <item>A <b>false alarm</b> is an alarm outside every such window.</item>
/// </list>
/// </summary>
public static class MotionJudge
{
    public static RecordingVerdict Run(
        SequenceHeader header,
        IEnumerable<SequenceFrame> frames,
        SequenceLabels labels,
        MotionSettings motion,
        JudgeSettings judge,
        MotionMask? mask = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(judge);
        judge.Validate();

        var detector = new MotionDetector(motion, mask);
        var motionAt = new List<TimeSpan>();
        var alarms = new List<TimeSpan>();
        TimeSpan? previous = null;
        TimeSpan? lastAlarm = null;
        var index = 0;
        var used = 0;
        var length = TimeSpan.Zero;
        foreach (var frame in frames)
        {
            var offset = frame.At - header.StartedAt;
            length = offset + header.FrameInterval;
            if (index++ % judge.FrameStep != 0)
            {
                continue;
            }

            used++;
            if (previous is { } before && offset - before > judge.MaxGap * judge.FrameStep)
            {
                detector.Reset();
            }

            previous = offset;
            if (!detector.Observe(frame.Luma).IsMotion)
            {
                continue;
            }

            motionAt.Add(offset);
            if (lastAlarm is null || offset - lastAlarm >= judge.Cooldown)
            {
                alarms.Add(offset);
                lastAlarm = offset;
            }
        }

        var windows = Windows(labels.People, judge);
        var people = windows
            .Select(w => new PersonOutcome(w.Person, motionAt.Where(m => m >= w.From && m < w.To).Select(m => (TimeSpan?)(m - w.Person.Start)).FirstOrDefault()))
            .ToList();
        var falseAlarms = alarms.Where(a => !windows.Any(w => a >= w.From && a < w.To)).ToList();
        return new RecordingVerdict(header.Name, length, used, people, alarms, falseAlarms);
    }

    /// <summary>
    /// Where motion counts for each stretch: from <see cref="JudgeSettings.Lead"/> before it to
    /// <see cref="JudgeSettings.Trail"/> after its last second, but never reaching back into the stretch before, so
    /// two visits close together do not share the same motion (seen in persona-cruza, 2026-11-07).
    /// </summary>
    private static List<(PersonInterval Person, TimeSpan From, TimeSpan To)> Windows(IReadOnlyList<PersonInterval> people, JudgeSettings judge)
    {
        var windows = new List<(PersonInterval, TimeSpan, TimeSpan)>();
        TimeSpan? previousEnd = null;
        foreach (var person in people.OrderBy(p => p.Start))
        {
            var from = person.Start - judge.Lead;
            if (previousEnd is { } end && end > from)
            {
                from = TimeSpan.FromTicks(Math.Min(end.Ticks, person.Start.Ticks));
            }

            var to = person.End + TimeSpan.FromSeconds(1) + judge.Trail;
            windows.Add((person, from, to));
            previousEnd = to;
        }

        return windows;
    }
}
