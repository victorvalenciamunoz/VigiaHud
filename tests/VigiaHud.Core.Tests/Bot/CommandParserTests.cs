using VigiaHud.Core.Bot;

namespace VigiaHud.Core.Tests.Bot;

public class CommandParserTests
{
    [Theory]
    [InlineData("/foto", BotCommand.Photo)]
    [InlineData("/estado", BotCommand.Status)]
    [InlineData("/ayuda", BotCommand.Help)]
    [InlineData("/start", BotCommand.Help)]
    [InlineData("/FOTO", BotCommand.Photo)]
    [InlineData("  /estado  ", BotCommand.Status)]
    [InlineData("/foto@VigiaHudBot", BotCommand.Photo)]
    [InlineData("/estado@VigiaHudBot ahora", BotCommand.Status)]
    [InlineData("/foto por favor", BotCommand.Photo)]
    [InlineData("/foto\nsegunda línea", BotCommand.Photo)]
    public void Reads_the_command_as_telegram_clients_send_it(string text, BotCommand expected) =>
        Assert.Equal(expected, CommandParser.Parse(text));

    [Theory]
    [InlineData("hola")]
    [InlineData("foto")]
    [InlineData("/fotos")]
    [InlineData("/armar")]
    [InlineData("/")]
    [InlineData("@VigiaHudBot /foto")]
    public void Any_other_text_is_unknown(string text) =>
        Assert.Equal(BotCommand.Unknown, CommandParser.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_text_is_no_command(string? text) =>
        Assert.Null(CommandParser.Parse(text));
}
