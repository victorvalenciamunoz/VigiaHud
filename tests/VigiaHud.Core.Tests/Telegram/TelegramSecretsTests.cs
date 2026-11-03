using VigiaHud.Core.Telegram;

namespace VigiaHud.Core.Tests.Telegram;

public class TelegramSecretsTests
{
    private const string Token = "123456789:TEST-token_not-real";

    [Fact]
    public void Reads_the_token_and_the_chat_id() =>
        Assert.Equal(new TelegramSecrets(Token, 111_222_333), TelegramSecrets.TryParse($$"""{"token":"{{Token}}","chatId":111222333}"""));

    [Fact]
    public void A_group_chat_id_is_negative_and_valid() =>
        Assert.Equal(-100_123, TelegramSecrets.TryParse($$"""{"token":"{{Token}}","chatId":-100123}""")!.ChatId);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"chatId":111222333}""")]
    [InlineData("""{"token":"123456789:TEST"}""")]
    [InlineData("""{"token":"123456789:TEST","chatId":"111222333"}""")]
    [InlineData("""{"token":"no-colon","chatId":111222333}""")]
    [InlineData("""{"token":"123:with space","chatId":111222333}""")]
    [InlineData("""{"token":"123456789:TEST","chatId":0}""")]
    public void Anything_else_is_no_secrets(string? json) =>
        Assert.Null(TelegramSecrets.TryParse(json));

    [Fact]
    public void Printing_it_never_shows_the_token() =>
        Assert.DoesNotContain(Token, new TelegramSecrets(Token, 111_222_333).ToString());
}
