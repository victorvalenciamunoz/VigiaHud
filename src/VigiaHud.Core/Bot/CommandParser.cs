namespace VigiaHud.Core.Bot;

/// <summary>
/// Reads a command from a message text. Accepts what Telegram clients actually send: the command first, maybe with
/// the bot's name after an at sign (<c>/foto@VigiaHudBot</c>, from the command menu), maybe with words after it,
/// in any case.
/// </summary>
public static class CommandParser
{
    private static readonly Dictionary<string, BotCommand> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/foto"] = BotCommand.Photo,
        ["/estado"] = BotCommand.Status,
        ["/ayuda"] = BotCommand.Help,
        ["/start"] = BotCommand.Help,
    };

    /// <summary>The command in <paramref name="text"/>; <see cref="BotCommand.Unknown"/> for any other text, null for no text.</summary>
    public static BotCommand? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var first = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var at = first.IndexOf('@');
        if (at > 0)
        {
            first = first[..at];
        }

        return Commands.TryGetValue(first, out var command) ? command : BotCommand.Unknown;
    }
}
