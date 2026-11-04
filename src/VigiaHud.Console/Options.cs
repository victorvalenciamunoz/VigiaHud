using System.Globalization;
using VigiaHud.Core.Recording;

namespace VigiaHud.Console;

/// <summary>A command line the owner got wrong: the message says how, in Spanish.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// The command line, read by hand: a command, at most one plain argument and a few <c>--name value</c> options. Not
/// worth a package (System.CommandLine) for two commands.
/// </summary>
public sealed class Options
{
    private readonly Dictionary<string, string> _named;

    private Options(string? command, string? argument, Dictionary<string, string> named)
    {
        Command = command;
        Argument = argument;
        _named = named;
    }

    public string? Command { get; }

    public string? Argument { get; }

    /// <summary>Where the recordings are: outside the repo, as they show the inside of a home (DECISIONS 024).</summary>
    public string Folder => _named.TryGetValue("carpeta", out var folder)
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vigiahud", "secuencias");

    public static Options Parse(string[] args)
    {
        string? command = null;
        string? argument = null;
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    throw new UsageException($"Falta el valor de {args[i]}.");
                }

                named[args[i][2..]] = args[++i];
            }
            else if (command is null)
            {
                command = args[i].ToLowerInvariant();
            }
            else if (argument is null)
            {
                argument = args[i];
            }
            else
            {
                throw new UsageException($"No sé qué hacer con «{args[i]}».");
            }
        }

        return new Options(command, argument, named);
    }

    public TimeSpan? Time(string name)
    {
        if (!_named.TryGetValue(name, out var text))
        {
            return null;
        }

        try
        {
            // The same notation as the labels file.
            return SequenceLabels.ParseOffset(text);
        }
        catch (FormatException)
        {
            throw new UsageException($"--{name} {text}: escribe la hora como mm:ss o h:mm:ss.");
        }
    }

    public int? Number(string name)
    {
        if (!_named.TryGetValue(name, out var text))
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new UsageException($"--{name} {text}: tiene que ser un número entero mayor que cero.");
    }

    /// <summary>The recordings in <see cref="Folder"/>, oldest first (their names start with the UTC instant).</summary>
    public IReadOnlyList<string> Recordings()
    {
        if (!Directory.Exists(Folder))
        {
            throw new UsageException($"No existe la carpeta {Folder}. Trae grabaciones con scripts\\pull-recordings.ps1.");
        }

        return [.. Directory.GetFiles(Folder, "*.vseq").Order(StringComparer.Ordinal)];
    }

    /// <summary>The one recording <see cref="Argument"/> names: a path, or a piece of a file name in the folder.</summary>
    public string Recording()
    {
        if (Argument is null)
        {
            throw new UsageException("Di qué grabación: el fichero .vseq o un trozo de su nombre.");
        }

        if (File.Exists(Argument))
        {
            return Path.GetFullPath(Argument);
        }

        var matches = Recordings().Where(p => Path.GetFileName(p).Contains(Argument, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new UsageException($"Ninguna grabación de {Folder} tiene «{Argument}» en el nombre."),
            _ => throw new UsageException($"Hay {matches.Count} grabaciones con «{Argument}» en el nombre: {string.Join(", ", matches.Select(Path.GetFileName))}."),
        };
    }
}
