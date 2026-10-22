using VigiaHud.Core.Watch;

namespace VigiaHud.Core.Tests.Watch;

public class WatchReporterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly WatchTimings Timings = WatchTimings.Default;

    [Fact]
    public void The_first_frame_earns_a_snapshot_and_nothing_else()
    {
        var reporter = new WatchReporter(Timings, Start);

        var tick = reporter.OnFrame(Start + TimeSpan.FromSeconds(2));

        Assert.Equal(new WatchTick(null, false, true), tick);
    }

    [Fact]
    public void Nothing_is_due_between_the_first_frame_and_the_first_minute()
    {
        var reporter = new WatchReporter(Timings, Start);
        reporter.OnFrame(Start + TimeSpan.FromSeconds(2));

        for (var seconds = 4; seconds < 60; seconds += 2)
        {
            Assert.Same(WatchTick.Nothing, reporter.OnFrame(Start + TimeSpan.FromSeconds(seconds)));
        }
    }

    [Fact]
    public void A_capture_report_is_due_after_a_minute_with_the_frames_and_failures_since_the_last_one()
    {
        var reporter = new WatchReporter(Timings, Start);
        var now = Start;
        for (var i = 0; i < 29; i++)
        {
            now += TimeSpan.FromSeconds(2);
            reporter.OnFrame(now);
        }

        now += TimeSpan.FromSeconds(2);
        var failed = reporter.OnFrameFailed(now);

        Assert.Equal(new CaptureReport(29, 1, TimeSpan.FromSeconds(60)), failed.Report);
        Assert.False(failed.Heartbeat);
        Assert.False(failed.Snapshot);

        // Counters start again from zero and the next report is a minute later.
        Assert.Same(WatchTick.Nothing, reporter.OnFrame(now + TimeSpan.FromSeconds(2)));
        var next = reporter.OnFrame(now + TimeSpan.FromSeconds(60));
        Assert.Equal(new CaptureReport(2, 0, TimeSpan.FromSeconds(60)), next.Report);
    }

    [Fact]
    public void Heartbeat_and_snapshot_are_due_every_ten_minutes()
    {
        var reporter = new WatchReporter(Timings, Start);
        reporter.OnFrame(Start + TimeSpan.FromSeconds(2));

        var before = reporter.OnFrame(Start + TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(2));
        Assert.False(before.Heartbeat);
        Assert.False(before.Snapshot);

        var at = reporter.OnFrame(Start + TimeSpan.FromMinutes(10));
        Assert.True(at.Heartbeat);
        Assert.False(at.Snapshot); // the snapshot clock started with the first frame, 2 s after the start

        var snapshot = reporter.OnFrame(Start + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2));
        Assert.True(snapshot.Snapshot);
        Assert.False(snapshot.Heartbeat);
    }

    [Fact]
    public void A_failed_frame_never_earns_a_snapshot_but_still_triggers_the_heartbeat()
    {
        var reporter = new WatchReporter(Timings, Start);

        var tick = reporter.OnFrameFailed(Start + TimeSpan.FromMinutes(10));

        Assert.False(tick.Snapshot);
        Assert.True(tick.Heartbeat);
        Assert.Equal(new CaptureReport(0, 1, TimeSpan.FromMinutes(10)), tick.Report);

        // The first good frame after that still gets the first snapshot.
        Assert.True(reporter.OnFrame(Start + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2)).Snapshot);
    }

    [Fact]
    public void Falling_behind_fires_each_action_once_and_schedules_the_next_from_now()
    {
        var reporter = new WatchReporter(Timings, Start);
        reporter.OnFrame(Start + TimeSpan.FromSeconds(2));

        // The phone slept for 35 minutes: one report, one heartbeat, one snapshot, not a burst of them.
        var late = Start + TimeSpan.FromMinutes(35);
        var tick = reporter.OnFrame(late);
        Assert.NotNull(tick.Report);
        Assert.Equal(2, tick.Report.Frames);
        Assert.True(tick.Heartbeat);
        Assert.True(tick.Snapshot);

        var nineLater = reporter.OnFrame(late + TimeSpan.FromMinutes(9));
        Assert.False(nineLater.Heartbeat);
        Assert.False(nineLater.Snapshot);
        var tenLater = reporter.OnFrame(late + TimeSpan.FromMinutes(10));
        Assert.True(tenLater.Heartbeat);
        Assert.True(tenLater.Snapshot);
    }

    [Fact]
    public void Uptime_is_measured_from_the_start() =>
        Assert.Equal(TimeSpan.FromMinutes(90), new WatchReporter(Timings, Start).Uptime(Start + TimeSpan.FromMinutes(90)));

    [Fact]
    public void Rejects_non_positive_intervals() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WatchReporter(Timings with { HeartbeatInterval = TimeSpan.Zero }, Start));
}
