using VigiaHud.Core.Bot;
using VigiaHud.Core.Device;

namespace VigiaHud.Core.Tests.Bot;

public class PowerWatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 2, 0, 0, TimeSpan.Zero);

    private readonly PowerWatch _watch = new();

    [Fact]
    public void Plugged_all_along_says_nothing_and_keeps_no_cut()
    {
        for (var minute = 0; minute < 30; minute++)
        {
            Assert.Empty(_watch.Observe(Plugged(90), T0.AddMinutes(minute)));
        }

        Assert.Empty(_watch.CutsSince(T0));
    }

    [Fact]
    public void Unplugged_ten_minutes_tells_once_since_when_and_the_battery()
    {
        for (var minute = 0; minute < 10; minute++)
        {
            Assert.Empty(_watch.Observe(Unplugged(84), T0.AddMinutes(minute)));
        }

        Assert.Equal([new PowerNotice.Lost(T0, 84)], _watch.Observe(Unplugged(84), T0.AddMinutes(10)));
        Assert.Empty(_watch.Observe(Unplugged(83), T0.AddMinutes(11)));
    }

    [Fact]
    public void A_short_cut_tells_nothing_but_is_kept_for_the_summary()
    {
        _watch.Observe(Unplugged(90), T0);
        Assert.Empty(_watch.Observe(Plugged(90), T0.AddMinutes(4)));

        Assert.Equal([new PowerCut(T0, T0.AddMinutes(4))], _watch.CutsSince(T0));
    }

    [Fact]
    public void The_end_of_a_told_cut_is_told_with_its_length_and_the_battery()
    {
        _watch.Observe(Unplugged(90), T0);
        _watch.Observe(Unplugged(88), T0.AddMinutes(10));

        var notices = _watch.Observe(Plugged(72), T0.AddMinutes(80));

        Assert.Equal([new PowerNotice.Back(new PowerCut(T0, T0.AddMinutes(80)), 72)], notices);
    }

    [Fact]
    public void Low_battery_without_charger_is_told_once_at_fifteen_percent()
    {
        _watch.Observe(Unplugged(16), T0);

        Assert.Equal([new PowerNotice.LowBattery(15)], _watch.Observe(Unplugged(15), T0.AddMinutes(1)));
        Assert.Empty(_watch.Observe(Unplugged(14), T0.AddMinutes(2)));
    }

    [Fact]
    public void Low_battery_is_told_again_after_charging_and_losing_power_once_more()
    {
        _watch.Observe(Unplugged(15), T0);
        _watch.Observe(Plugged(15), T0.AddMinutes(1));

        Assert.Equal([new PowerNotice.LowBattery(15)], _watch.Observe(Unplugged(15), T0.AddMinutes(2)));
    }

    [Fact]
    public void Low_battery_while_charging_says_nothing() =>
        Assert.Empty(_watch.Observe(Plugged(10), T0));

    [Fact]
    public void A_lost_exemption_is_told_once_and_again_only_after_coming_back()
    {
        Assert.Equal([new PowerNotice.DozeExemptionLost()], _watch.Observe(Plugged(90, doze: false), T0));
        Assert.Empty(_watch.Observe(Plugged(90, doze: false), T0.AddMinutes(1)));
        Assert.Empty(_watch.Observe(Plugged(90, doze: null), T0.AddMinutes(2)));
        Assert.Empty(_watch.Observe(Plugged(90, doze: true), T0.AddMinutes(3)));

        Assert.Equal([new PowerNotice.DozeExemptionLost()], _watch.Observe(Plugged(90, doze: false), T0.AddMinutes(4)));
    }

    [Fact]
    public void Old_cuts_are_forgotten()
    {
        _watch.Observe(Unplugged(90), T0);
        _watch.Observe(Plugged(90), T0.AddMinutes(5));
        _watch.Observe(Unplugged(90), T0.AddHours(30));
        _watch.Observe(Plugged(90), T0.AddHours(30).AddMinutes(2));

        Assert.Equal([new PowerCut(T0.AddHours(30), T0.AddHours(30).AddMinutes(2))], _watch.CutsSince(T0.AddHours(24)));
    }

    [Theory]
    [InlineData(100, 11)]
    [InlineData(84, 9)]
    [InlineData(15, 2)]
    [InlineData(9, 1)]
    [InlineData(4, 0)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void Hours_left_follow_the_measured_nine_percent_an_hour(int percent, int hours) =>
        Assert.Equal(hours, PowerWatch.HoursLeft(percent));

    private static DeviceStatus Plugged(int percent, bool? doze = true) => new(percent, true, 25, doze);

    private static DeviceStatus Unplugged(int percent, bool? doze = true) => new(percent, false, 25, doze);
}
