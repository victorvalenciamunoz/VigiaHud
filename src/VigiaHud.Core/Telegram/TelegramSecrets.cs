using System.Text.Json;

namespace VigiaHud.Core.Telegram;

/// <summary>
/// The bot's token and the owner's chat id, as the phone keeps them: a small JSON file in the app's private storage,
/// copied there from the PC's user secrets by <c>scripts/push-telegram-secrets.ps1</c> (DECISIONS 021). Never in the
/// repo, never in the APK, never in a log line.
/// </summary>
public sealed record TelegramSecrets(string Token, long ChatId)
{
    public const string FileName = "telegram.json";

    /// <summary>Reads <c>{"token": "...", "chatId": 123}</c>; null for anything else, without saying what was there.</summary>
    public static TelegramSecrets? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("chatId", out var chat) || chat.ValueKind != JsonValueKind.Number
                || !chat.TryGetInt64(out var chatId))
            {
                return null;
            }

            var value = token.GetString()!.Trim();
            return value.Contains(':') && !value.Any(char.IsWhiteSpace) && chatId != 0
                ? new TelegramSecrets(value, chatId)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Never the token: a record's default text would print it.</summary>
    public override string ToString() => $"TelegramSecrets(chat set: {ChatId != 0})";
}
