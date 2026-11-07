using VigiaHud.Core.Judge;
using VigiaHud.Core.Motion;
using VigiaHud.Core.Recording;
using static VigiaHud.Core.Tests.Support.SyntheticFrames;

namespace VigiaHud.Core.Tests.Judge;

/// <summary>
/// The judge on made-up recordings, one frame a second: a still wall, with a square walking across it in the seconds
/// given. With the default detector, motion starts at the second frame of a walk.
/// </summary>
public class MotionJudgeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly SequenceHeader Header = new("escena", Start, Width, Height, 320, 240, 90, TimeSpan.FromSeconds(1));

    [Fact]
    public void A_still_room_without_anybody_gives_nothing()
    {
        var verdict = Judge(Scene(60), "# sin nadie");

        Assert.Empty(verdict.Alarms);
        Assert.Empty(verdict.People);
        Assert.Equal(60, verdict.FramesUsed);
        Assert.Equal(TimeSpan.FromSeconds(60), verdict.Length);
    }

    [Fact]
    public void Somebody_walking_is_detected_one_second_into_their_stretch()
    {
        var verdict = Judge(Scene(60, walks: (10, 15)), "persona 00:10-00:15");

        var person = Assert.Single(verdict.People);
        Assert.True(person.Detected);
        Assert.Equal(TimeSpan.FromSeconds(1), person.FirstMotion);
        Assert.Equal([TimeSpan.FromSeconds(11)], verdict.Alarms);
        Assert.Empty(verdict.FalseAlarms);
        Assert.Equal((1, 0), (verdict.Detected, verdict.Missed));
    }

    [Fact]
    public void Somebody_who_does_not_move_is_missed()
    {
        var verdict = Judge(Scene(60), "persona 00:10-00:15");

        Assert.False(Assert.Single(verdict.People).Detected);
        Assert.Equal((0, 1), (verdict.Detected, verdict.Missed));
    }

    [Fact]
    public void Motion_without_anybody_is_a_false_alarm()
    {
        var verdict = Judge(Scene(60, walks: (30, 35)), "# sin nadie");

        Assert.Equal([TimeSpan.FromSeconds(31)], verdict.FalseAlarms);
    }

    [Fact]
    public void Motion_a_little_before_somebody_comes_in_counts_for_them()
    {
        // A shadow walks in at 7-8 s; the person is labelled from 10 s, within the 5 s lead.
        var verdict = Judge(Scene(60, walks: (7, 9)), "persona 00:10-00:15");

        var person = Assert.Single(verdict.People);
        Assert.Equal(TimeSpan.FromSeconds(-2), person.FirstMotion);
        Assert.Empty(verdict.FalseAlarms);
    }

    [Fact]
    public void Motion_long_before_somebody_comes_in_is_a_false_alarm()
    {
        // Motion at 2-4 s (the square gone at 4 s is change too); the lead opens at 5 s.
        var verdict = Judge(Scene(60, walks: (1, 3)), "persona 00:10-00:15");

        Assert.Equal([TimeSpan.FromSeconds(2)], verdict.FalseAlarms);
        Assert.False(Assert.Single(verdict.People).Detected);
    }

    [Fact]
    public void Within_the_cooldown_a_second_visit_is_detected_but_raises_no_alarm()
    {
        var verdict = Judge(Scene(120, walks: [(10, 15), (60, 65)]), "persona 00:10-00:15\npersona 01:00-01:05");

        Assert.Equal([TimeSpan.FromSeconds(11)], verdict.Alarms);
        Assert.All(verdict.People, p => Assert.True(p.Detected));
    }

    [Fact]
    public void After_the_cooldown_a_new_visit_raises_a_new_alarm()
    {
        var verdict = Judge(Scene(200, walks: [(10, 15), (140, 145)]), "persona 00:10-00:15\npersona 02:20-02:25");

        Assert.Equal([TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(141)], verdict.Alarms);
        Assert.Empty(verdict.FalseAlarms);
    }

    [Fact]
    public void A_false_alarm_within_the_cooldown_of_a_true_one_is_not_counted_again()
    {
        // The walk at 40 s is nobody (not labelled), but no alarm goes out for it: still within 2 min of the first.
        var verdict = Judge(Scene(120, walks: [(10, 15), (40, 45)]), "persona 00:10-00:15");

        Assert.Empty(verdict.FalseAlarms);
    }

    [Fact]
    public void Two_visits_close_together_do_not_share_their_motion()
    {
        // Motion all along from 11 s. The second visit's lead would open at 13 s, inside the first one's trail (to 15 s):
        // it opens at 15 s instead.
        var verdict = Judge(Scene(60, walks: (10, 30)), "persona 00:10-00:12\npersona 00:18-00:20");

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-3)], verdict.People.Select(p => p.FirstMotion));
    }

    [Fact]
    public void Every_other_frame_is_the_watch_at_one_every_two_seconds()
    {
        var verdict = Judge(Scene(60, walks: (10, 15)), "persona 00:10-00:15", JudgeSettings.Default with { FrameStep = 2 });

        Assert.Equal(30, verdict.FramesUsed);
        Assert.True(Assert.Single(verdict.People).Detected);
        Assert.Equal(TimeSpan.FromSeconds(2), verdict.People[0].FirstMotion);
    }

    [Fact]
    public void A_gap_in_the_camera_starts_the_detector_again()
    {
        // The square shows up in the first frame after a 20 s gap and is gone in the next. Compared across the gap,
        // that is two frames of change in a row, motion; started again, the first one is only a new reference.
        var frames = Scene(10).Concat(Scene(10, offset: 30, walks: [(30, 30)])).ToList();

        var verdict = Judge(frames, "# sin nadie");

        Assert.Empty(verdict.Alarms);
    }

    [Fact]
    public void A_mask_over_the_walk_hides_it()
    {
        // The square walks along y 40-60 of 120, the middle row of a 1x3 grid.
        var mask = new MotionMask(1, 3, [(0, 1)]);

        var verdict = MotionJudge.Run(Header, Scene(60, walks: (30, 35)), SequenceLabels.Parse("# sin nadie"), MotionSettings.Default, JudgeSettings.Default, mask);

        Assert.Empty(verdict.Alarms);
    }

    private static RecordingVerdict Judge(IEnumerable<SequenceFrame> frames, string labels, JudgeSettings? judge = null) =>
        MotionJudge.Run(Header, frames, SequenceLabels.Parse(labels), MotionSettings.Default, judge ?? JudgeSettings.Default);

    private static IEnumerable<SequenceFrame> Scene(int seconds, int offset = 0, params (int From, int To)[] walks)
    {
        for (var s = offset; s < offset + seconds; s++)
        {
            var walking = walks.FirstOrDefault(w => s >= w.From && s <= w.To);
            (int, int)? square = walking == default ? null : (10 + ((s - walking.From) * 22 % 130), 40);
            yield return new SequenceFrame(Start.AddSeconds(s), Wall(seed: s, square: square), []);
        }
    }

    private static IEnumerable<SequenceFrame> Scene(int seconds, (int From, int To) walks) => Scene(seconds, 0, walks);
}
