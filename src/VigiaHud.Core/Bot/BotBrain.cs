namespace VigiaHud.Core.Bot;

/// <summary>
/// Decides what to do with each message (CLAUDE.md: "Solo obedece a su dueño"). Anything not from the owner's chat is
/// ignored without a word, so a stranger who finds the bot learns nothing, and leaves a line in the log with the chat
/// and the length of the text, never the text itself.
/// </summary>
public sealed class BotBrain
{
    private readonly long _ownerChatId;

    public BotBrain(long ownerChatId)
    {
        if (ownerChatId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerChatId), ownerChatId, "The owner's chat id is required.");
        }

        _ownerChatId = ownerChatId;
    }

    public BotDecision Decide(IncomingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ChatId != _ownerChatId)
        {
            var length = message.Text?.Length ?? 0;
            return new BotDecision.Ignore($"telegram: ignored message from chat {message.ChatId} ({length} chars): not the owner");
        }

        return CommandParser.Parse(message.Text) switch
        {
            null => new BotDecision.Ignore(null),
            BotCommand.Photo => new BotDecision.SendPhoto(),
            BotCommand.Status => new BotDecision.SendStatus(),
            BotCommand.Help => new BotDecision.Reply(BotTexts.Help),
            _ => new BotDecision.Reply(BotTexts.Unknown),
        };
    }
}
