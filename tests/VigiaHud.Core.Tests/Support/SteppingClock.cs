using Microsoft.Extensions.Time.Testing;

namespace VigiaHud.Core.Tests.Support;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that tells when code starts waiting on it. Advancing the time before the code
/// under test has created its timer loses the advance (seen on 2026-10-29: a test hung once in a while), so tests
/// wait for the timer first and only then move the clock.
/// </summary>
public sealed class SteppingClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly SemaphoreSlim _created = new(0);
    private int _timers;

    /// <summary>How many timers (each <c>Task.Delay</c> on this clock is one) have been created so far.</summary>
    public int TimersCreated => Volatile.Read(ref _timers);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);
        _created.Release();
        return timer;
    }

    /// <summary>Waits (in real time, briefly) until <paramref name="count"/> timers exist in total.</summary>
    public async Task WaitForTimersAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (TimersCreated < count)
        {
            await _created.WaitAsync(deadline.Token);
        }
    }
}
