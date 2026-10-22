using VigiaHud.Core.Logging;

namespace VigiaHud.Core.Tests.Logging;

public sealed class LogReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Reads_nothing_when_there_is_no_log_yet() =>
        Assert.Empty(LogReader.ReadAll(new RotatingFileLogOptions(_directory)));

    [Fact]
    public void Reads_rotated_files_oldest_first_and_skips_torn_lines()
    {
        var options = new RotatingFileLogOptions(_directory) { MaxFiles = 3 };
        Directory.CreateDirectory(_directory);
        File.WriteAllText(options.RotatedPath(2), "2026-10-22T14:00:00.000Z\tstart\tone\n");
        File.WriteAllText(options.RotatedPath(1), "2026-10-22T14:01:00.000Z\tcapture\ttwo\ngarbage line\n");
        File.WriteAllText(options.CurrentPath, "2026-10-22T14:02:00.000Z\tstop\tthree\n2026-10-22T14:03:0");

        var entries = LogReader.ReadAll(options);

        Assert.Equal(["one", "two", "three"], entries.Select(e => e.Message));
        Assert.Equal([LogKind.Start, LogKind.Capture, LogKind.Stop], entries.Select(e => e.Kind));
    }

    [Fact]
    public void Read_last_keeps_the_newest_entries_in_order()
    {
        var options = new RotatingFileLogOptions(_directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllLines(options.CurrentPath, Enumerable.Range(1, 5).Select(i => $"2026-10-22T14:0{i}:00.000Z\tinfo\tn{i}"));

        Assert.Equal(["n4", "n5"], LogReader.ReadLast(options, 2).Select(e => e.Message));
        Assert.Equal(5, LogReader.ReadLast(options, 10).Count);
        Assert.Empty(LogReader.ReadLast(options, 0));
    }
}
