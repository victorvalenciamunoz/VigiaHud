using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VigiaHud.Core.Bot;

namespace VigiaHud.Core.Telegram;

/// <summary>
/// The Bot API over plain HTTPS (DECISIONS 016): JSON bodies for text, <c>multipart/form-data</c> for the photo, and
/// <see cref="JsonDocument"/> to read the answers (no reflection: the app is trimmed). Each request carries its own
/// deadline, so the <see cref="HttpClient"/> given must not impose a shorter one: create it with
/// <c>Timeout = Timeout.InfiniteTimeSpan</c>. The token goes only into the request path, never into a message.
/// </summary>
public sealed class TelegramClient : ITelegramClient
{
    /// <summary>
    /// Joined as text, not with <c>new Uri(base, relative)</c>: "bot123:ABC" looks like a URI with scheme "bot123",
    /// and the base would be dropped (caught by a test on 2026-10-29).
    /// </summary>
    private const string ApiBase = "https://api.telegram.org/bot";

    /// <summary>How much longer than the long poll itself to wait for its answer before calling it a timeout.</summary>
    public static readonly TimeSpan DefaultPollMargin = TimeSpan.FromSeconds(15);

    /// <summary>Deadline for a message or a photo; a photo is small (DECISIONS 008), a slow link still gets a minute.</summary>
    private static readonly TimeSpan SendDeadline = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly TimeSpan _pollMargin;

    /// <param name="http">With an infinite timeout: each request sets its own deadline.</param>
    /// <param name="token">The bot token from @BotFather.</param>
    /// <param name="pollMargin">Extra wait over a long poll before giving up; <see cref="DefaultPollMargin"/> unless testing.</param>
    public TelegramClient(HttpClient http, string token, TimeSpan? pollMargin = null)
    {
        _pollMargin = pollMargin ?? DefaultPollMargin;
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || !token.Contains(':'))
        {
            // Not echoed: a wrong token may still be most of a real one.
            throw new ArgumentException("The bot token is missing or malformed.", nameof(token));
        }

        _http = http;
        _token = token;
    }

    public async Task<UpdateBatch> GetUpdatesAsync(long? offset, TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait), wait, "The wait cannot be negative.");
        }

        var body = new JsonObject
        {
            ["timeout"] = (int)wait.TotalSeconds,
            // Only messages: edits, reactions and the rest are not commands.
            ["allowed_updates"] = new JsonArray("message"),
        };
        if (offset is { } value)
        {
            body["offset"] = value;
        }

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var document = await CallAsync("getUpdates", content, wait + _pollMargin, cancellationToken).ConfigureAwait(false);
        return ReadUpdates(document.RootElement.GetProperty("result"));
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var body = new JsonObject { ["chat_id"] = chatId, ["text"] = text };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var _ = await CallAsync("sendMessage", content, SendDeadline, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendPhotoAsync(long chatId, byte[] jpeg, string? caption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length == 0)
        {
            throw new ArgumentException("The photo is empty.", nameof(jpeg));
        }

        using var content = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString(CultureInfo.InvariantCulture)), "chat_id" },
        };
        if (!string.IsNullOrEmpty(caption))
        {
            content.Add(new StringContent(caption, Encoding.UTF8), "caption");
        }

        var photo = new ByteArrayContent(jpeg);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(photo, "photo", "foto.jpg");

        using var _ = await CallAsync("sendPhoto", content, SendDeadline, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts to a Bot API method and returns the answer when it says <c>"ok": true</c>. Every other outcome becomes
    /// one of the two exceptions of <see cref="ITelegramClient"/>; a cancellation from the caller stays a cancellation.
    /// </summary>
    private async Task<JsonDocument> CallAsync(string method, HttpContent content, TimeSpan deadline, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.PostAsync(new Uri($"{ApiBase}{_token}/{method}"), content, timeout.Token).ConfigureAwait(false);
            using (response)
            {
                text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TelegramUnavailableException($"{method}: no answer in {deadline.TotalSeconds:0} s");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            // The inner exception is not kept: only this message, with the token hidden, may reach the log.
            throw new TelegramUnavailableException($"{method}: {Hide(error.Message)}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            // A proxy or a load balancer answered, not the Bot API (an HTML 502, for instance).
            throw new TelegramUnavailableException($"{method}: HTTP {(int)response.StatusCode} without a Bot API answer");
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
            && root.TryGetProperty("result", out _))
        {
            return document;
        }

        using (document)
        {
            throw ReadError(root, (int)response.StatusCode, method);
        }
    }

    private static TelegramApiException ReadError(JsonElement root, int status, string method)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new TelegramApiException(status, $"{method}: unexpected answer", null);
        }

        var code = root.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var parsed) ? parsed : status;
        var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()!
            : "no description";
        TimeSpan? retryAfter = root.TryGetProperty("parameters", out var p)
            && p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty("retry_after", out var r) && r.TryGetInt32(out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;
        return new TelegramApiException(code, description, retryAfter);
    }

    private static UpdateBatch ReadUpdates(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            return UpdateBatch.Empty;
        }

        var messages = new List<IncomingMessage>();
        long last = 0;
        foreach (var update in result.EnumerateArray())
        {
            var id = update.GetProperty("update_id").GetInt64();
            last = Math.Max(last, id);

            // Anything that is not a message still moves the offset, so it is not delivered again.
            if (!update.TryGetProperty("message", out var message)
                || !message.TryGetProperty("chat", out var chat)
                || !chat.TryGetProperty("id", out var chatId))
            {
                continue;
            }

            var text = message.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var sentAt = message.TryGetProperty("date", out var date) && date.TryGetInt64(out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.UnixEpoch;
            messages.Add(new IncomingMessage(id, chatId.GetInt64(), text, sentAt));
        }

        return new UpdateBatch(messages, last + 1);
    }

    /// <summary>Network errors should not carry the URL, but if one ever does, the token must not reach the log.</summary>
    private string Hide(string message) => message.Replace(_token, "<token>", StringComparison.Ordinal);
}
