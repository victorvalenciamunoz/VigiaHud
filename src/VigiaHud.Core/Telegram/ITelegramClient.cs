using VigiaHud.Core.Bot;

namespace VigiaHud.Core.Telegram;

/// <summary>
/// The three calls of the Telegram Bot API the watch uses (DECISIONS 016). Behind an interface so the bot logic is
/// tested with a fake and the real client with recorded answers.
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    /// Long polling: waits up to <paramref name="wait"/> for new messages after <paramref name="offset"/>. Asking with
    /// a higher offset confirms the earlier ones, so Telegram does not send them again.
    /// </summary>
    /// <exception cref="TelegramApiException">Telegram answered with an error.</exception>
    /// <exception cref="TelegramUnavailableException">Telegram could not be reached, or did not answer in time.</exception>
    Task<UpdateBatch> GetUpdatesAsync(long? offset, TimeSpan wait, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetUpdatesAsync" path="/exception"/>
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetUpdatesAsync" path="/exception"/>
    Task SendPhotoAsync(long chatId, byte[] jpeg, string? caption, CancellationToken cancellationToken);
}

/// <summary>What one poll brought.</summary>
/// <param name="Messages">The messages, oldest first.</param>
/// <param name="NextOffset">The offset for the next poll (last update id + 1); null if nothing came.</param>
public sealed record UpdateBatch(IReadOnlyList<IncomingMessage> Messages, long? NextOffset)
{
    public static readonly UpdateBatch Empty = new([], null);
}

/// <summary>Telegram answered, and the answer was an error (<c>"ok": false</c>).</summary>
public sealed class TelegramApiException(int errorCode, string description, TimeSpan? retryAfter)
    : Exception($"Telegram error {errorCode}: {description}")
{
    /// <summary>Telegram's <c>error_code</c>, which follows HTTP: 401 bad token, 409 another poller, 429 too many requests.</summary>
    public int ErrorCode { get; } = errorCode;

    public string Description { get; } = description;

    /// <summary>How long to wait before asking again, when Telegram says it (flood control, 429).</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Telegram could not be reached: no network, DNS, TLS, a timeout, or a server error without a Bot API answer.
/// The message never contains the token.
/// </summary>
public sealed class TelegramUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
