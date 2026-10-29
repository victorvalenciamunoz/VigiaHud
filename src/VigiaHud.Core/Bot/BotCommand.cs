namespace VigiaHud.Core.Bot;

/// <summary>What the owner can ask for (PLAN.md, phase 2). Arming and disarming come in phase 5.</summary>
public enum BotCommand
{
    /// <summary><c>/foto</c>: a picture of the room now.</summary>
    Photo,

    /// <summary><c>/estado</c>: battery, charger, temperature, time running, last daily heartbeat.</summary>
    Status,

    /// <summary><c>/ayuda</c> (and <c>/start</c>, which Telegram sends when the chat is opened): the list of commands.</summary>
    Help,

    /// <summary>Any other text: answered with a short pointer to the help.</summary>
    Unknown,
}
