using VigiaHud.Core.Logging;

namespace VigiaHud.Core.Tests.Logging;

public class LogLineTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 22, 14, 40, 46, 123, TimeSpan.Zero);

    [Fact]
    public void Formats_utc_instant_kind_and_message_separated_by_tabs() =>
        Assert.Equal("2026-10-22T14:40:46.123Z\theartbeat\tbattery=97 temp=33.5",
            LogLine.Format(new LogEntry(Instant, LogKind.Heartbeat, "battery=97 temp=33.5")));

    [Fact]
    public void A_local_instant_is_written_in_utc()
    {
        var madrid = new DateTimeOffset(2026, 10, 22, 16, 40, 46, 123, TimeSpan.FromHours(2));

        Assert.StartsWith("2026-10-22T14:40:46.123Z\t", LogLine.Format(new LogEntry(madrid, LogKind.Info, "x")));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("with\ttab")]
    [InlineData("two\nlines\r\nand more")]
    [InlineData("back\\slash and \\n literal")]
    [InlineData("acentos y ñ")]
    public void Round_trips_any_message_on_one_line(string message)
    {
        var line = LogLine.Format(new LogEntry(Instant, LogKind.Error, message));

        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.Equal(2, line.Count(c => c == '\t'));
        Assert.True(LogLine.TryParse(line, out var parsed));
        Assert.Equal(message, parsed.Message);
        Assert.Equal(LogKind.Error, parsed.Kind);
        Assert.Equal(Instant, parsed.At);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-22T14:40:46.123Z\theartbeat")]
    [InlineData("not a date\theartbeat\tx")]
    [InlineData("2026-10-22T14:40:46.123Z\tunknownkind\tx")]
    [InlineData("2026-10-22T14:40:46.123Z\t7\tx")]
    [InlineData("2026-10-22T14:40:4")]
    public void Rejects_torn_or_foreign_lines(string? line) =>
        Assert.False(LogLine.TryParse(line, out _));
}
