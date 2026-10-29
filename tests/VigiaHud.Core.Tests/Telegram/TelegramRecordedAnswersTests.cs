using System.Net;
using VigiaHud.Core.Bot;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Telegram;

/// <summary>
/// The client against real Bot API answers, recorded on 2026-10-29 with scripts/record-telegram-fixtures.cs and
/// with every personal value replaced (the owner is 111222333, "Dueño"). The 429 answer is not here: flood control
/// cannot be provoked safely, so it is covered with the documented shape in <see cref="TelegramClientTests"/>.
/// </summary>
public class TelegramRecordedAnswersTests
{
    private const string Token = "123456789:TEST-token_not-real";
    private const long Owner = 111_222_333;

    [Fact]
    public async Task Real_messages_are_read_text_or_not_and_the_offset_moves_past_them()
    {
        var batch = await Client(HttpStatusCode.OK, "getUpdates-messages.json").GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(["/start", "/foto", "🍒", null], batch.Messages.Select(m => m.Text));
        Assert.All(batch.Messages, m => Assert.Equal(Owner, m.ChatId));
        Assert.Equal(190_431_668, batch.NextOffset);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_793_278_104), batch.Messages[0].SentAt);
    }

    [Fact]
    public async Task Real_messages_lead_to_the_expected_decisions()
    {
        var batch = await Client(HttpStatusCode.OK, "getUpdates-messages.json").GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None);
        var brain = new BotBrain(Owner);

        var decisions = batch.Messages.Select(brain.Decide).ToList();

        Assert.Equal(new BotDecision.Reply(BotTexts.Help), decisions[0]);
        Assert.IsType<BotDecision.SendPhoto>(decisions[1]);
        Assert.Equal(new BotDecision.Reply(BotTexts.Unknown), decisions[2]);
        Assert.Equal(new BotDecision.Ignore(null), decisions[3]);
    }

    [Fact]
    public async Task A_real_empty_poll_is_an_empty_batch()
    {
        var batch = await Client(HttpStatusCode.OK, "getUpdates-empty.json").GetUpdatesAsync(190_431_668, TimeSpan.Zero, CancellationToken.None);

        Assert.Same(UpdateBatch.Empty, batch);
    }

    [Fact]
    public async Task A_real_sent_message_is_accepted() =>
        await Client(HttpStatusCode.OK, "sendMessage-ok.json").SendMessageAsync(Owner, "Prueba.", CancellationToken.None);

    [Fact]
    public async Task A_real_sent_photo_is_accepted() =>
        await Client(HttpStatusCode.OK, "sendPhoto-ok.json").SendPhotoAsync(Owner, [0xFF, 0xD8, 0xFF, 0xD9], "Foto de prueba.", CancellationToken.None);

    [Fact]
    public async Task A_real_bad_token_answer_is_an_api_error_401()
    {
        var error = await Assert.ThrowsAsync<TelegramApiException>(
            () => Client(HttpStatusCode.Unauthorized, "error-401-unauthorized.json").GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(401, error.ErrorCode);
        Assert.Equal("Unauthorized", error.Description);
    }

    [Fact]
    public async Task A_real_second_poller_answer_is_an_api_error_409()
    {
        var error = await Assert.ThrowsAsync<TelegramApiException>(
            () => Client(HttpStatusCode.Conflict, "error-409-conflict.json").GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(409, error.ErrorCode);
        Assert.StartsWith("Conflict: terminated by other getUpdates request", error.Description);
        Assert.Null(error.RetryAfter);
    }

    private static TelegramClient Client(HttpStatusCode status, string fixture)
    {
        var body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Telegram", fixture));
        var handler = new FakeHttpHandler().Answer(status, body);
        return new TelegramClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, Token);
    }
}
