namespace VigiaHud.Core.Bot;

/// <summary>What to do with an incoming message. The app carries it out; Core only decides.</summary>
public abstract record BotDecision
{
    private BotDecision()
    {
    }

    /// <summary>Not from the owner, or nothing to answer: say nothing to Telegram, and write <see cref="LogLine"/> if any.</summary>
    public sealed record Ignore(string? LogLine) : BotDecision;

    /// <summary>Answer with a text.</summary>
    public sealed record Reply(string Text) : BotDecision;

    /// <summary>Answer with a picture of the room now, taken from the camera stream.</summary>
    public sealed record SendPhoto : BotDecision;

    /// <summary>Answer with the status text, built from the device at the moment of answering.</summary>
    public sealed record SendStatus : BotDecision;
}
