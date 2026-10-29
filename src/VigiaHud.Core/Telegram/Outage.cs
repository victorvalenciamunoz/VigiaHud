namespace VigiaHud.Core.Telegram;

/// <summary>
/// A stretch of time in which Telegram could not be reached: what the judge of phase 2 measures (cut the router's
/// wifi for 10 minutes, see that the watch comes back on its own and says so).
/// </summary>
/// <param name="Start">The first failed poll, UTC.</param>
/// <param name="End">The first poll that worked again, UTC.</param>
/// <param name="FailedAttempts">Polls that failed in between, the first one included.</param>
/// <param name="LastReason">Why the last one failed, without the token.</param>
public sealed record Outage(DateTimeOffset Start, DateTimeOffset End, int FailedAttempts, string LastReason)
{
    public TimeSpan Duration => End - Start;
}
