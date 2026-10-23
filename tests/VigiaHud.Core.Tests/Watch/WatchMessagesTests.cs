using System.Globalization;
using VigiaHud.Core.Device;
using VigiaHud.Core.Watch;

namespace VigiaHud.Core.Tests.Watch;

public class WatchMessagesTests
{
    [Fact]
    public void Capture_line_is_key_value_pairs() =>
        Assert.Equal("frames=30 failed=1 period=60s", WatchMessages.Capture(new CaptureReport(30, 1, TimeSpan.FromSeconds(60.4))));

    [Fact]
    public void Heartbeat_line_has_uptime_battery_plug_temperature_and_doze_exemption() =>
        Assert.Equal("uptime=1h02m battery=97% plugged=yes temp=33.5C doze_exempt=yes",
            WatchMessages.Heartbeat(new DeviceStatus(97, true, 33.5, true), TimeSpan.FromMinutes(62)));

    [Fact]
    public void A_lost_doze_exemption_is_said_in_the_heartbeat() =>
        Assert.EndsWith(" doze_exempt=no", WatchMessages.Heartbeat(new DeviceStatus(50, true, 30.0, false), TimeSpan.Zero));

    [Fact]
    public void Unknown_battery_temperature_and_exemption_are_question_marks() =>
        Assert.Equal("uptime=0m battery=? plugged=no temp=? doze_exempt=?",
            WatchMessages.Heartbeat(new DeviceStatus(null, false, null), TimeSpan.Zero));

    [Fact]
    public void Numbers_use_a_point_whatever_the_thread_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        try
        {
            Assert.Contains("temp=33.5C", WatchMessages.Heartbeat(new DeviceStatus(50, true, 33.5), TimeSpan.Zero));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(6, "6m")]
    [InlineData(59, "59m")]
    [InlineData(60, "1h00m")]
    [InlineData(62, "1h02m")]
    [InlineData(24 * 60, "1d00h00m")]
    [InlineData(2 * 24 * 60 + 63, "2d01h03m")]
    [InlineData(-5, "0m")]
    public void Uptime_is_compact(int minutes, string expected) =>
        Assert.Equal(expected, WatchMessages.FormatUptime(TimeSpan.FromMinutes(minutes)));
}
