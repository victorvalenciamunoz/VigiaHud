namespace VigiaHud.Core.Logging;

/// <summary>
/// Where the log lives and how big it may grow. With one capture tick a minute and a heartbeat every ten,
/// a day is about 100 KB, so the defaults keep several weeks on the phone.
/// </summary>
public sealed record RotatingFileLogOptions(string Directory)
{
    public const string DefaultFileName = "vigia.log";

    public string FileName { get; init; } = DefaultFileName;

    /// <summary>When the current file passes this size, it is rotated before the next line is written.</summary>
    public long MaxFileBytes { get; init; } = 1024 * 1024;

    /// <summary>The current file plus the rotated ones; the oldest is deleted when a new rotation would exceed this.</summary>
    public int MaxFiles { get; init; } = 5;

    public string CurrentPath => Path.Combine(Directory, FileName);

    /// <summary>The rotated files, newest first: vigia.1.log, vigia.2.log...</summary>
    public string RotatedPath(int index)
    {
        var name = Path.GetFileNameWithoutExtension(FileName);
        var extension = Path.GetExtension(FileName);
        return Path.Combine(Directory, $"{name}.{index}{extension}");
    }
}
