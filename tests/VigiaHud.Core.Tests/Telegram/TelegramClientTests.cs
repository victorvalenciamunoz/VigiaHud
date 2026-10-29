using System.Net;
using System.Text.Json;
using VigiaHud.Core.Telegram;
using VigiaHud.Core.Tests.Support;

namespace VigiaHud.Core.Tests.Telegram;

public class TelegramClientTests
{
    private const string Token = "123456789:TEST-token_not-real";
    private const long Chat = 111_222_333;

    private readonly FakeHttpHandler _handler = new();
    private readonly TelegramClient _client;

    public TelegramClientTests()
    {
        _client = new TelegramClient(new HttpClient(_handler) { Timeout = Timeout.InfiniteTimeSpan }, Token);
    }

    [Fact]
    public async Task Get_updates_asks_for_messages_only_with_offset_and_long_poll_timeout()
    {
        _handler.Answer(HttpStatusCode.OK, """{"ok":true,"result":[]}""");

        await _client.GetUpdatesAsync(42, TimeSpan.FromSeconds(50), CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/getUpdates", request.Uri.ToString());
        Assert.Equal("application/json", request.ContentType);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(42, body.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(50, body.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal("message", Assert.Single(body.RootElement.GetProperty("allowed_updates").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task The_first_poll_sends_no_offset()
    {
        _handler.Answer(HttpStatusCode.OK, """{"ok":true,"result":[]}""");

        await _client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(_handler.Requests).Body);
        Assert.False(body.RootElement.TryGetProperty("offset", out _));
    }

    [Fact]
    public async Task Nothing_new_is_an_empty_batch_that_keeps_the_offset()
    {
        _handler.Answer(HttpStatusCode.OK, """{"ok":true,"result":[]}""");

        var batch = await _client.GetUpdatesAsync(7, TimeSpan.Zero, CancellationToken.None);

        Assert.Empty(batch.Messages);
        Assert.Null(batch.NextOffset);
    }

    [Fact]
    public async Task Messages_are_read_with_chat_text_and_date_and_the_offset_moves_past_the_last()
    {
        _handler.Answer(HttpStatusCode.OK, """
            {"ok":true,"result":[
              {"update_id":500,"message":{"message_id":1,"date":1761739200,"chat":{"id":111222333,"type":"private"},"text":"/foto"}},
              {"update_id":501,"message":{"message_id":2,"date":1761739260,"chat":{"id":111222333,"type":"private"},"sticker":{"emoji":"👍"}}},
              {"update_id":503,"edited_message":{"message_id":1,"date":1761739200,"chat":{"id":111222333,"type":"private"},"text":"/estado"}}
            ]}
            """);

        var batch = await _client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(2, batch.Messages.Count);
        Assert.Equal(new(500, Chat, "/foto", DateTimeOffset.FromUnixTimeSeconds(1761739200)), batch.Messages[0]);
        Assert.Null(batch.Messages[1].Text);
        Assert.Equal(504, batch.NextOffset);
    }

    [Fact]
    public async Task Send_message_posts_chat_and_text_as_json()
    {
        _handler.Answer(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":9}}""");

        await _client.SendMessageAsync(Chat, "Sigo vivo.", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.EndsWith("/sendMessage", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(Chat, body.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("Sigo vivo.", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Send_photo_uploads_the_jpeg_as_multipart_with_chat_and_caption()
    {
        _handler.Answer(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":10}}""");

        await _client.SendPhotoAsync(Chat, [0xFF, 0xD8, 0xFF, 0xD9], "Foto de las 12:51.", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.EndsWith("/sendPhoto", request.Uri.AbsolutePath);
        Assert.Equal("multipart/form-data", request.ContentType);
        Assert.Contains("name=chat_id", request.Body);
        Assert.Contains("111222333", request.Body);
        Assert.Contains("name=caption", request.Body);
        Assert.Contains("Foto de las 12:51.", request.Body);
        Assert.Contains("name=photo; filename=foto.jpg", request.Body);
        Assert.Contains("Content-Type: image/jpeg", request.Body);
    }

    [Fact]
    public async Task A_bot_api_error_keeps_its_code_description_and_retry_after()
    {
        _handler.Answer(HttpStatusCode.TooManyRequests,
            """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 35","parameters":{"retry_after":35}}""");

        var error = await Assert.ThrowsAsync<TelegramApiException>(
            () => _client.SendMessageAsync(Chat, "hola", CancellationToken.None));

        Assert.Equal(429, error.ErrorCode);
        Assert.Equal("Too Many Requests: retry after 35", error.Description);
        Assert.Equal(TimeSpan.FromSeconds(35), error.RetryAfter);
    }

    [Fact]
    public async Task An_error_without_retry_after_has_none()
    {
        _handler.Answer(HttpStatusCode.Unauthorized, """{"ok":false,"error_code":401,"description":"Unauthorized"}""");

        var error = await Assert.ThrowsAsync<TelegramApiException>(
            () => _client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(401, error.ErrorCode);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task A_non_bot_api_answer_is_unavailable_not_an_api_error()
    {
        _handler.Answer(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>");

        var error = await Assert.ThrowsAsync<TelegramUnavailableException>(
            () => _client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.Contains("HTTP 502", error.Message);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_and_never_shows_the_token()
    {
        _handler.Throw(new HttpRequestException($"failed to reach https://api.telegram.org/bot{Token}/getUpdates"));

        var error = await Assert.ThrowsAsync<TelegramUnavailableException>(
            () => _client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.DoesNotContain(Token, error.Message);
        Assert.Contains("<token>", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_poll_that_never_answers_times_out_as_unavailable()
    {
        _handler.Hang();
        var client = new TelegramClient(new HttpClient(_handler) { Timeout = Timeout.InfiniteTimeSpan }, Token, TimeSpan.FromSeconds(1));

        // A zero-second poll gets only the margin; the fake hangs until our own deadline cancels it.
        var error = await Assert.ThrowsAsync<TelegramUnavailableException>(
            () => client.GetUpdatesAsync(null, TimeSpan.Zero, CancellationToken.None));

        Assert.Contains("no answer in 1 s", error.Message);
    }

    [Fact]
    public void The_real_margin_outlasts_a_slow_link() =>
        Assert.Equal(TimeSpan.FromSeconds(15), TelegramClient.DefaultPollMargin);

    [Fact]
    public async Task Cancelling_from_outside_stays_a_cancellation()
    {
        _handler.Hang();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _client.GetUpdatesAsync(null, TimeSpan.FromSeconds(50), stop.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-colon")]
    [InlineData("123:with space")]
    public void A_malformed_token_is_refused_without_echoing_it(string token)
    {
        var error = Assert.Throws<ArgumentException>(() => new TelegramClient(new HttpClient(), token));

        if (token.Trim().Length > 0)
        {
            Assert.DoesNotContain(token, error.Message);
        }
    }
}
