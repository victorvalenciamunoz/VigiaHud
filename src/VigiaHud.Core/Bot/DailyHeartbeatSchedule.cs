namespace VigiaHud.Core.Bot;

/// <summary>When the next daily heartbeat goes out, and whether it is a late one.</summary>
/// <param name="SendAt">When to send it, UTC; now or earlier means at once.</param>
/// <param name="Late">It is making up for one that could not go out at its time (DECISIONS 019).</param>
/// <param name="DueAt">The time it was meant for, UTC: today's or the last missed one.</param>
public sealed record HeartbeatPlan(DateTimeOffset SendAt, bool Late, DateTimeOffset DueAt);

/// <summary>
/// The daily "sigo vivo" at a fixed local time (09:00, DECISIONS 015), in the phone's zone, daylight saving
/// included. A day's heartbeat counts as given by anything sent at or after its time. If one was missed (phone
/// off, or the service down at that time) it goes out as soon as possible, marked as late. Nothing is owed before
/// the first one ever: a new install waits for the next 09:00.
/// </summary>
public sealed class DailyHeartbeatSchedule
{
    /// <summary>A timer waking a little after the hour is on time, not late.</summary>
    private static readonly TimeSpan OnTimeMargin = TimeSpan.FromMinutes(5);

    private readonly TimeOnly _at;
    private readonly TimeZoneInfo _zone;

    public DailyHeartbeatSchedule(TimeOnly at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _at = at;
        _zone = zone;
    }

    public TimeOnly At => _at;

    /// <summary>The heartbeat time on a local date, as a UTC instant.</summary>
    public DateTimeOffset DueOn(DateOnly localDate)
    {
        var local = localDate.ToDateTime(_at, DateTimeKind.Unspecified);
        // The hour that does not exist when clocks go forward: take the first one that does.
        while (_zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero);
    }

    public HeartbeatPlan Plan(DateTimeOffset now, DateTimeOffset? lastSent)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime);
        var dueToday = DueOn(today);
        var dueYesterday = DueOn(today.AddDays(-1));
        var lastDue = now >= dueToday ? dueToday : dueYesterday;

        // First run ever, or the last due one already given: the next one is on time.
        if (lastSent is null || lastSent >= lastDue)
        {
            var next = now < dueToday ? dueToday : DueOn(today.AddDays(1));
            return new HeartbeatPlan(next, false, next);
        }

        // One is owed: now. Late only if this is clearly after its time.
        return new HeartbeatPlan(now, now - lastDue > OnTimeMargin, lastDue);
    }
}
