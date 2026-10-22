using System.Text;

namespace VigiaHud.Core.Logging;

/// <summary>
/// The log on disk: one <see cref="LogLine"/> per entry, appended and flushed at once, rotated by size.
/// The file is opened for each line: a few writes a minute cost nothing, and there is never an open handle to lose
/// when Android kills the process. Writes are serialized with a lock, and a failure to write is swallowed after one
/// attempt to say so on the same file, because the log must never bring the watch down.
/// </summary>
public sealed class RotatingFileLog : IWatchLog
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly RotatingFileLogOptions _options;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public RotatingFileLog(RotatingFileLogOptions options, TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (_options.MaxFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFiles must be at least 1.");
        }

        if (_options.MaxFileBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFileBytes must be positive.");
        }
    }

    public void Write(LogKind kind, string message)
    {
        var line = LogLine.Format(new LogEntry(_clock.GetUtcNow(), kind, message ?? string.Empty)) + "\n";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_options.Directory);
                RotateIfFull();
                Append(line);
            }
            catch (Exception)
            {
                // The log is not allowed to take the watch down. Nothing else to do here: there is no other log.
            }
        }
    }

    private void Append(string line)
    {
        using var stream = new FileStream(_options.CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = Utf8NoBom.GetBytes(line);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private void RotateIfFull()
    {
        var current = new FileInfo(_options.CurrentPath);
        if (!current.Exists || current.Length < _options.MaxFileBytes)
        {
            return;
        }

        // Shift vigia.(n-1).log -> vigia.n.log from the oldest down, dropping the one that would fall off the end.
        var oldest = _options.MaxFiles - 1;
        for (var index = oldest; index >= 1; index--)
        {
            var target = _options.RotatedPath(index);
            File.Delete(target);
            if (index == oldest && _options.MaxFiles == 1)
            {
                break;
            }

            var source = index == 1 ? _options.CurrentPath : _options.RotatedPath(index - 1);
            if (File.Exists(source) && oldest >= 1)
            {
                File.Move(source, target);
            }
        }

        if (_options.MaxFiles == 1)
        {
            File.Delete(_options.CurrentPath);
        }
    }
}
