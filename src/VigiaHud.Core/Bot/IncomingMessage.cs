namespace VigiaHud.Core.Bot;

/// <summary>
/// A text message the bot received, reduced to what the watch needs: who sent it (the chat), what it says and when.
/// <see cref="UpdateId"/> is Telegram's counter, used to acknowledge the message so it is not delivered again.
/// </summary>
/// <param name="UpdateId">Telegram's update identifier; the next poll asks for the ones after it.</param>
/// <param name="ChatId">The chat it came from. Only the owner's chat is obeyed.</param>
/// <param name="Text">The text, or null for anything that is not text (a sticker, a photo, a join).</param>
/// <param name="SentAt">When Telegram says it was sent, in UTC.</param>
public sealed record IncomingMessage(long UpdateId, long ChatId, string? Text, DateTimeOffset SentAt);
