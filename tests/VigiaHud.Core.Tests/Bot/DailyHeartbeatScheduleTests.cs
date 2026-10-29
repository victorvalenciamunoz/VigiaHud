using VigiaHud.Core.Bot;

namespace VigiaHud.Core.Tests.Bot;

public class DailyHeartbeatScheduleTests
{
    // One hour ahead of UTC, two in summer, changing on the last Sundays of March and October: a generic zone with
    // daylight saving, so the tests do not depend on the machine's zone and name no place.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "Test DST", TimeSpan.FromHours(1), "Test DST", "Test", "Test summer",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    private readonly DailyHeartbeatSchedule _schedule = new(new TimeOnly(9, 0), Zone);

    // 2026-10-29 is in winter time: 09:00 local is 08:00 UTC.
    private static readonly DateTimeOffset NineToday = Utc(2026, 10, 29, 8, 0);
    private static readonly DateTimeOffset NineYesterday = Utc(2026, 10, 28, 8, 0);
    private static readonly DateTimeOffset NineTomorrow = Utc(2026, 10, 30, 8, 0);

    [Fact]
    public void Before_nine_with_yesterdays_given_the_next_is_today_at_nine() =>
        Assert.Equal(new HeartbeatPlan(NineToday, false, NineToday), _schedule.Plan(Utc(2026, 10, 29, 6, 0), NineYesterday));

    [Fact]
    public void After_nine_with_todays_given_the_next_is_tomorrow() =>
        Assert.Equal(new HeartbeatPlan(NineTomorrow, false, NineTomorrow), _schedule.Plan(Utc(2026, 10, 29, 14, 0), NineToday));

    [Fact]
    public void At_nine_it_is_due_now_and_on_time()
    {
        var now = NineToday.AddSeconds(30);

        Assert.Equal(new HeartbeatPlan(now, false, NineToday), _schedule.Plan(now, NineYesterday));
    }

    [Fact]
    public void Missed_at_nine_it_goes_out_now_marked_late()
    {
        var now = Utc(2026, 10, 29, 9, 30); // 10:30 local, phone back after a night without battery

        Assert.Equal(new HeartbeatPlan(now, true, NineToday), _schedule.Plan(now, NineYesterday));
    }

    [Fact]
    public void Missed_yesterday_it_goes_out_now_even_before_todays()
    {
        var now = Utc(2026, 10, 29, 6, 0);

        Assert.Equal(new HeartbeatPlan(now, true, NineYesterday), _schedule.Plan(now, NineYesterday.AddDays(-1)));
    }

    [Fact]
    public void After_a_late_one_today_still_has_its_own()
    {
        var lateForYesterday = Utc(2026, 10, 29, 6, 0);

        Assert.Equal(NineToday, _schedule.Plan(lateForYesterday.AddMinutes(1), lateForYesterday).SendAt);
    }

    [Theory]
    [InlineData(6, 0, 2026, 10, 29)]   // before nine: today
    [InlineData(14, 0, 2026, 10, 30)]  // after nine: tomorrow, nothing owed on a new install
    public void A_new_install_owes_nothing_and_waits_for_the_next_nine(int hour, int minute, int year, int month, int day)
    {
        var plan = _schedule.Plan(Utc(2026, 10, 29, hour, minute), null);

        Assert.False(plan.Late);
        Assert.Equal(_schedule.DueOn(new DateOnly(year, month, day)), plan.SendAt);
    }

    [Fact]
    public void Nine_stays_nine_local_across_the_autumn_clock_change()
    {
        Assert.Equal(Utc(2026, 10, 24, 7, 0), _schedule.DueOn(new DateOnly(2026, 10, 24))); // summer: +2
        Assert.Equal(Utc(2026, 10, 25, 8, 0), _schedule.DueOn(new DateOnly(2026, 10, 25))); // the change was at 03:00
        Assert.Equal(Utc(2026, 10, 26, 8, 0), _schedule.DueOn(new DateOnly(2026, 10, 26))); // winter: +1
    }

    [Fact]
    public void An_hour_that_does_not_exist_moves_to_the_first_that_does()
    {
        var schedule = new DailyHeartbeatSchedule(new TimeOnly(2, 30), Zone);

        // 2026-03-29: clocks jump from 02:00 to 03:00; 02:30 never happens, 03:00 summer is 01:00 UTC.
        Assert.Equal(Utc(2026, 3, 29, 1, 0), schedule.DueOn(new DateOnly(2026, 3, 29)));
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
