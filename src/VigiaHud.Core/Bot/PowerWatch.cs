using VigiaHud.Core.Device;

namespace VigiaHud.Core.Bot;

/// <summary>A stretch without charger: from the first look that found it unplugged to the first that found it plugged.</summary>
public sealed record PowerCut(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Something about power the owner should hear (DECISIONS 019).</summary>
public abstract record PowerNotice
{
    private PowerNotice()
    {
    }

    /// <summary>Unplugged for <see cref="PowerWatch.NoticeAfter"/>: the charger was pulled or the power went.</summary>
    public sealed record Lost(DateTimeOffset Since, int? BatteryPercent) : PowerNotice;

    /// <summary>Plugged again after a cut the owner was told about.</summary>
    public sealed record Back(PowerCut Cut, int? BatteryPercent) : PowerNotice;

    /// <summary>On battery and down to <see cref="PowerWatch.LowBatteryPercent"/> or less: the phone will die soon.</summary>
    public sealed record LowBattery(int BatteryPercent) : PowerNotice;

    /// <summary>The battery-optimization exemption is gone (it is lost at every reboot): Android may put the watch to sleep.</summary>
    public sealed record DozeExemptionLost : PowerNotice;
}

/// <summary>
/// Watches the charger, the battery and the battery-optimization exemption from regular looks at the phone (the bot's
/// clock looks once a minute) and says when the owner should hear about them, once each (DECISIONS 019): a cut after
/// 10 minutes unplugged, its end if the start was told, a low battery at 15 % while unplugged, and a lost exemption.
/// Every cut, short or long, is kept for the daily summary. No clock of its own: the time comes with each look.
/// </summary>
public sealed class PowerWatch
{
    public static readonly TimeSpan NoticeAfter = TimeSpan.FromMinutes(10);
    public const int LowBatteryPercent = 15;

    /// <summary>Battery used per hour with the camera always on, measured on the OPPO (DECISIONS 014).</summary>
    public const double BatteryPercentPerHour = 9;

    private readonly List<PowerCut> _cuts = [];
    private DateTimeOffset? _unpluggedSince;
    private bool _lostTold;
    private bool _lowTold;
    private bool _dozeTold;

    /// <summary>A look at the phone at <paramref name="now"/>; returns what to tell, usually nothing.</summary>
    public IReadOnlyList<PowerNotice> Observe(DeviceStatus device, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);
        var notices = new List<PowerNotice>();

        if (device.IsPluggedIn)
        {
            if (_unpluggedSince is { } since)
            {
                var cut = new PowerCut(since, now);
                _cuts.Add(cut);
                if (_lostTold)
                {
                    notices.Add(new PowerNotice.Back(cut, device.BatteryPercent));
                }
            }

            _unpluggedSince = null;
            _lostTold = false;
            _lowTold = false;
        }
        else
        {
            _unpluggedSince ??= now;
            if (!_lostTold && now - _unpluggedSince >= NoticeAfter)
            {
                notices.Add(new PowerNotice.Lost(_unpluggedSince.Value, device.BatteryPercent));
                _lostTold = true;
            }

            if (!_lowTold && device.BatteryPercent is { } percent && percent <= LowBatteryPercent)
            {
                notices.Add(new PowerNotice.LowBattery(percent));
                _lowTold = true;
            }
        }

        // Told once while it stays lost; told again if it comes back and is lost later.
        if (device.IsBatteryOptimizationIgnored == false && !_dozeTold)
        {
            notices.Add(new PowerNotice.DozeExemptionLost());
            _dozeTold = true;
        }
        else if (device.IsBatteryOptimizationIgnored == true)
        {
            _dozeTold = false;
        }

        return notices;
    }

    /// <summary>The cuts that ended after <paramref name="since"/>, oldest first; older ones are forgotten.</summary>
    public IReadOnlyList<PowerCut> CutsSince(DateTimeOffset since)
    {
        _cuts.RemoveAll(c => c.End < since);
        return [.. _cuts];
    }

    /// <summary>
    /// Rough hours left at the measured drain, rounded to the hour; under one hour is zero. A simple model on purpose:
    /// it can be off by an hour or two, and what matters is the order of magnitude (DECISIONS 019).
    /// </summary>
    public static int HoursLeft(int batteryPercent) => (int)Math.Round(Math.Max(batteryPercent, 0) / BatteryPercentPerHour, MidpointRounding.AwayFromZero);
}
