using VigiaHud.Core.Logging;
using VigiaHud.Core.Telegram;

namespace VigiaHud.Core.Bot;

/// <summary>
/// The whole bot, so the app only has to start it (CLAUDE.md: the app orchestrates, Core decides). Two loops: the
/// poller, which answers the owner's commands and reports outages, and the clock, which writes the daily heartbeat
/// and sends whatever waits in the outbox. Answers to commands go straight out (a failed answer makes the command
/// come again); everything the bot says on its own goes through the <see cref="Outbox"/>.
/// </summary>
public sealed class BotRunner
{
    public static readonly TimeOnly DefaultHeartbeatAt = new(9, 0);

    /// <summary>Outages shorter than this are not reported to the owner, only logged and counted daily (DECISIONS 019).</summary>
    public static readonly TimeSpan MinOutageToNotify = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The clock loop never sleeps longer than this and asks the wall clock again on waking: a timer may not count
    /// while the CPU sleeps, the wall clock always does.
    /// </summary>
    private static readonly TimeSpan MaxNap = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private readonly ITelegramClient _client;
    private readonly long _owner;
    private readonly IBotEnvironment _environment;
    private readonly IBotMemory _memory;
    private readonly TimeProvider _clock;
    private readonly IWatchLog _log;
    private readonly BotBrain _brain;
    private readonly TelegramPoller _poller;
    private readonly DailyHeartbeatSchedule _schedule;
    private readonly List<Outage> _outages = [];
    private readonly PowerWatch _power = new();

    public BotRunner(
        ITelegramClient client,
        long ownerChatId,
        IBotEnvironment environment,
        IBotMemory memory,
        TimeProvider clock,
        IWatchLog log,
        TimeOnly? heartbeatAt = null,
        TimeSpan? pollWait = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _client = client;
        _owner = ownerChatId;
        _environment = environment;
        _memory = memory;
        _clock = clock;
        _log = log;
        _brain = new BotBrain(ownerChatId);
        _poller = new TelegramPoller(client, clock, log, pollWait);
        _schedule = new DailyHeartbeatSchedule(heartbeatAt ?? DefaultHeartbeatAt, environment.Zone);
        Outbox = new Outbox(client, ownerChatId, clock, log, environment.Zone);
    }

    /// <summary>Where the bot's own messages wait for the network: heartbeat, outage, power and battery notices.</summary>
    public Outbox Outbox { get; }

    /// <summary>Runs both loops until cancelled; never ends on its own.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log.Write(LogKind.Info, "telegram: bot started");
        await Task.WhenAll(
            _poller.RunAsync(HandleAsync, ReconnectedAsync, cancellationToken),
            ClockAsync(cancellationToken)).ConfigureAwait(false);
    }

    private async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        switch (_brain.Decide(message))
        {
            case BotDecision.Ignore ignore:
                if (ignore.LogLine is { } line)
                {
                    _log.Write(LogKind.Info, line);
                }

                return;

            case BotDecision.Reply reply:
                await _client.SendMessageAsync(_owner, reply.Text, cancellationToken).ConfigureAwait(false);
                break;

            case BotDecision.SendStatus:
                var status = BotTexts.Status(_environment.ReadDevice(), _environment.WatchingFor, _memory.LastDailyHeartbeat, _clock.GetUtcNow(), _environment.Zone);
                await _client.SendMessageAsync(_owner, status, cancellationToken).ConfigureAwait(false);
                break;

            case BotDecision.SendPhoto:
                var jpeg = await _environment.TakePhotoAsync(cancellationToken).ConfigureAwait(false);
                if (jpeg is null)
                {
                    await _client.SendMessageAsync(_owner, BotTexts.CameraNotReady, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var caption = BotTexts.PhotoCaption(_clock.GetUtcNow(), _environment.Zone);
                    await _client.SendPhotoAsync(_owner, jpeg, caption, cancellationToken).ConfigureAwait(false);
                }

                break;
        }

        // The owner just reached us: Telegram works, a good moment for what was waiting.
        await Outbox.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconnectedAsync(Outage outage, CancellationToken cancellationToken)
    {
        lock (_outages)
        {
            _outages.Add(outage);
        }

        if (IsWorthNotifying(outage))
        {
            Outbox.Enqueue(BotTexts.Reconnected(outage, _environment.Zone));
        }

        await Outbox.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Only outages of <see cref="MinOutageToNotify"/> or more get a message; Wi-Fi hiccups only go to the log and the daily count.</summary>
    public static bool IsWorthNotifying(Outage outage)
    {
        ArgumentNullException.ThrowIfNull(outage);
        return outage.Duration >= MinOutageToNotify;
    }

    /// <summary>
    /// Once a minute at least: looks at the phone for power and battery notices, writes the daily heartbeat when due,
    /// and sends what waits in the outbox.
    /// </summary>
    private async Task ClockAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var nap = MaxNap;
            try
            {
                var now = _clock.GetUtcNow();
                var device = _environment.ReadDevice();
                foreach (var notice in _power.Observe(device, now))
                {
                    Tell(notice, now);
                }

                var plan = _schedule.Plan(now, _memory.LastDailyHeartbeat);
                if (plan.SendAt <= now)
                {
                    Outbox.Enqueue(BotTexts.DailyHeartbeat(device, _environment.WatchingFor, OutagesSince(now - Day), _power.CutsSince(now - Day), plan, _environment.Zone));
                    _memory.LastDailyHeartbeat = now;
                    _log.Write(LogKind.Info, plan.Late ? "telegram: daily heartbeat written (late)" : "telegram: daily heartbeat written");
                }
                else if (plan.SendAt - now < nap)
                {
                    nap = plan.SendAt - now;
                }

                if (Outbox.Count > 0)
                {
                    await Outbox.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // The heartbeat is the proof of life: a fault here is logged, and the clock keeps going.
                _log.Write(LogKind.Error, $"telegram: clock failed: {error.GetType().Name}: {error.Message}");
            }

            try
            {
                await Task.Delay(nap, _clock, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Queues a power notice for the owner and leaves a line in the log, which outlives the queue.</summary>
    private void Tell(PowerNotice notice, DateTimeOffset now)
    {
        var (text, line) = notice switch
        {
            PowerNotice.Lost lost => (BotTexts.PowerLost(lost, now, _environment.Zone), $"power: unplugged since {lost.Since:yyyy-MM-ddTHH:mm:ssZ}, battery {lost.BatteryPercent}%"),
            PowerNotice.Back back => (BotTexts.PowerBack(back), $"power: back after {back.Cut.Duration.TotalMinutes:0} min, battery {back.BatteryPercent}%"),
            PowerNotice.LowBattery low => (BotTexts.LowBattery(low), $"power: battery low, {low.BatteryPercent}% without charger"),
            PowerNotice.DozeExemptionLost => (BotTexts.DozeWarning, "power: battery optimization exemption lost"),
            _ => throw new InvalidOperationException($"Unknown notice {notice.GetType().Name}"),
        };

        Outbox.Enqueue(text);
        _log.Write(LogKind.Info, line);
    }

    private List<Outage> OutagesSince(DateTimeOffset since)
    {
        lock (_outages)
        {
            _outages.RemoveAll(o => o.End < since);
            return [.. _outages];
        }
    }
}
