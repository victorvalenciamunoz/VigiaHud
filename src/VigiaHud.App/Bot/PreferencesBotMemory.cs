using System.Globalization;
using VigiaHud.Core.Bot;

namespace VigiaHud.App.Bot;

/// <summary>What the bot must remember across restarts, in the app's preferences (DECISIONS 019): the last heartbeat.</summary>
public sealed class PreferencesBotMemory : IBotMemory
{
    private const string LastHeartbeatKey = "bot_last_heartbeat";

    public DateTimeOffset? LastDailyHeartbeat
    {
        get => DateTimeOffset.TryParse(Preferences.Default.Get(LastHeartbeatKey, string.Empty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
        set
        {
            if (value is { } instant)
            {
                Preferences.Default.Set(LastHeartbeatKey, instant.ToString("O", CultureInfo.InvariantCulture));
            }
            else
            {
                Preferences.Default.Remove(LastHeartbeatKey);
            }
        }
    }
}
