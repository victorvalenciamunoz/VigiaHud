using VigiaHud.Core.Recording;

namespace VigiaHud.Core.Tests.Recording;

public class RecordingCommandTests
{
    [Theory]
    [InlineData("/grabar 2 salida-1", 120, "salida-1")]
    [InlineData("/grabar 2m salida-1", 120, "salida-1")]
    [InlineData("/grabar 2min salida-1", 120, "salida-1")]
    [InlineData("/grabar 90s", 90, RecordingCommand.DefaultName)]
    [InlineData("/grabar 8h casa-vacia", 8 * 3600, "casa-vacia")]
    [InlineData("/grabar 12H Casa-Vacia", 12 * 3600, "casa-vacia")]
    [InlineData("/grabar@VigiaHudBot 10s luz2", 10, "luz2")]
    [InlineData("  /grabar   3   x  ", 180, "x")]
    public void A_length_and_a_name_start_a_recording(string text, int seconds, string name) =>
        Assert.Equal(new RecordingCommand.Start(TimeSpan.FromSeconds(seconds), name), RecordingCommand.Parse(text));

    [Theory]
    [InlineData("/grabar")]
    [InlineData("/grabar   ")]
    public void The_command_alone_asks_how_it_is_used(string text) =>
        Assert.IsType<RecordingCommand.Usage>(RecordingCommand.Parse(text));

    [Theory]
    [InlineData("/grabar parar")]
    [InlineData("/grabar PARAR")]
    public void Parar_stops_the_recording(string text) =>
        Assert.IsType<RecordingCommand.Stop>(RecordingCommand.Parse(text));

    [Theory]
    [InlineData("/grabar salida-1", RecordingProblem.BadDuration)]
    [InlineData("/grabar 2x", RecordingProblem.BadDuration)]
    [InlineData("/grabar -2", RecordingProblem.BadDuration)]
    [InlineData("/grabar 1,5h", RecordingProblem.BadDuration)]
    [InlineData("/grabar 9s", RecordingProblem.TooShort)]
    [InlineData("/grabar 0", RecordingProblem.TooShort)]
    [InlineData("/grabar 13h", RecordingProblem.TooLong)]
    [InlineData("/grabar 721", RecordingProblem.TooLong)]
    [InlineData("/grabar 2 salida 1", RecordingProblem.BadName)]
    [InlineData("/grabar 2 salón", RecordingProblem.BadName)]
    [InlineData("/grabar 2 salida_1", RecordingProblem.BadName)]
    [InlineData("/grabar 2 -salida", RecordingProblem.BadName)]
    [InlineData("/grabar 2 ../log", RecordingProblem.BadName)]
    [InlineData("/grabar 2 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", RecordingProblem.BadName)]
    public void Anything_else_says_what_is_wrong(string text, RecordingProblem problem) =>
        Assert.Equal(new RecordingCommand.Invalid(problem), RecordingCommand.Parse(text));

    [Fact]
    public void The_longest_name_allowed_is_forty_characters() =>
        Assert.IsType<RecordingCommand.Start>(RecordingCommand.Parse("/grabar 2 " + new string('a', 40)));

    [Fact]
    public void Twelve_hours_is_allowed() =>
        Assert.Equal(new RecordingCommand.Start(TimeSpan.FromHours(12), RecordingCommand.DefaultName), RecordingCommand.Parse("/grabar 720"));
}
