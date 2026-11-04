using System.Globalization;
using System.Text.RegularExpressions;

namespace VigiaHud.Core.Recording;

/// <summary>Why a <c>/grabar</c> could not be read; <see cref="Bot.BotTexts"/> says it in Spanish.</summary>
public enum RecordingProblem
{
    BadDuration,
    TooShort,
    TooLong,
    BadName,
}

/// <summary>
/// What the owner asked with <c>/grabar</c> (DECISIONS 024): record a test scene of a given length and name, stop the
/// one running, or nothing understood. Only for building the phase 3 test recordings; the watch itself never records.
/// </summary>
public abstract partial record RecordingCommand
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    /// <summary>One night with nobody home; at about 110 MB an hour, longer would not fit the phone's free space.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

    public const int MaxNameLength = 40;

    public const string DefaultName = "escena";

    private RecordingCommand()
    {
    }

    /// <summary><c>/grabar 2 salida-1</c>, <c>/grabar 90s</c>, <c>/grabar 8h casa-vacia</c>.</summary>
    public sealed record Start(TimeSpan Duration, string Name) : RecordingCommand;

    /// <summary><c>/grabar parar</c>.</summary>
    public sealed record Stop : RecordingCommand;

    /// <summary><c>/grabar</c> alone: say how it is used.</summary>
    public sealed record Usage : RecordingCommand;

    public sealed record Invalid(RecordingProblem Problem) : RecordingCommand;

    /// <summary>
    /// Reads the words after the command. The length is a whole number with an optional unit (<c>s</c>, <c>m</c>,
    /// <c>min</c>, <c>h</c>; minutes without one). The name, optional, is one word of plain letters, digits and dashes,
    /// because it becomes part of a file name; it is lowercased.
    /// </summary>
    public static RecordingCommand Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var arguments = words.Skip(1).ToArray();
        if (arguments.Length == 0)
        {
            return new Usage();
        }

        if (arguments.Length == 1 && arguments[0].Equals("parar", StringComparison.OrdinalIgnoreCase))
        {
            return new Stop();
        }

        var match = DurationPattern().Match(arguments[0]);
        if (!match.Success)
        {
            return new Invalid(RecordingProblem.BadDuration);
        }

        var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        var duration = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(amount),
            "h" => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromMinutes(amount),
        };

        if (duration < MinDuration)
        {
            return new Invalid(RecordingProblem.TooShort);
        }

        if (duration > MaxDuration)
        {
            return new Invalid(RecordingProblem.TooLong);
        }

        if (arguments.Length > 2)
        {
            return new Invalid(RecordingProblem.BadName);
        }

        var name = arguments.Length == 2 ? arguments[1].ToLowerInvariant() : DefaultName;
        return name.Length <= MaxNameLength && NamePattern().IsMatch(name)
            ? new Start(duration, name)
            : new Invalid(RecordingProblem.BadName);
    }

    [GeneratedRegex(@"^(?<amount>\d{1,5})(?<unit>s|m|min|h)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
