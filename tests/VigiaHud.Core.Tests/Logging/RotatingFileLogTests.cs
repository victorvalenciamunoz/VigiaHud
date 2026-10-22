using Microsoft.Extensions.Time.Testing;
using VigiaHud.Core.Logging;

namespace VigiaHud.Core.Tests.Logging;

public sealed class RotatingFileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Creates_the_directory_and_appends_one_line_per_write()
    {
        var options = new RotatingFileLogOptions(_directory);
        var log = new RotatingFileLog(options, _clock);

        log.Write(LogKind.Start, "app opened");
        _clock.Advance(TimeSpan.FromMinutes(1));
        log.Write(LogKind.Capture, "frames=30");

        var lines = File.ReadAllLines(options.CurrentPath);
        Assert.Equal(["2026-10-22T14:00:00.000Z\tstart\tapp opened", "2026-10-22T14:01:00.000Z\tcapture\tframes=30"], lines);
    }

    [Fact]
    public void Each_line_is_on_disk_as_soon_as_write_returns()
    {
        var options = new RotatingFileLogOptions(_directory);
        var log = new RotatingFileLog(options, _clock);

        log.Write(LogKind.Error, "boom");

        // No handle is kept: the file can be read and even deleted by someone else right away.
        Assert.Single(File.ReadAllLines(options.CurrentPath));
        File.Delete(options.CurrentPath);
        log.Write(LogKind.Info, "after delete");
        Assert.Equal(["2026-10-22T14:00:00.000Z\tinfo\tafter delete"], File.ReadAllLines(options.CurrentPath));
    }

    [Fact]
    public void Rotates_when_the_current_file_is_full_and_keeps_at_most_max_files()
    {
        // Each line is 41 bytes; a 100-byte limit rotates after the third line.
        var options = new RotatingFileLogOptions(_directory) { MaxFileBytes = 100, MaxFiles = 3 };
        var log = new RotatingFileLog(options, _clock);

        for (var i = 1; i <= 10; i++)
        {
            log.Write(LogKind.Capture, $"frames={i:00}");
        }

        Assert.True(File.Exists(options.CurrentPath));
        Assert.True(File.Exists(options.RotatedPath(1)));
        Assert.True(File.Exists(options.RotatedPath(2)));
        Assert.False(File.Exists(options.RotatedPath(3)));
        Assert.Equal(3, Directory.GetFiles(_directory).Length);

        // Nothing is lost inside the kept window and the order is preserved across files.
        var kept = LogReader.ReadAll(options).Select(e => e.Message).ToList();
        Assert.Equal(["frames=04", "frames=05", "frames=06", "frames=07", "frames=08", "frames=09", "frames=10"], kept);
        Assert.Equal(["frames=10"], File.ReadAllLines(options.CurrentPath).Select(l => l.Split('\t')[2]));
    }

    [Fact]
    public void With_a_single_file_the_full_file_is_simply_truncated()
    {
        var options = new RotatingFileLogOptions(_directory) { MaxFileBytes = 100, MaxFiles = 1 };
        var log = new RotatingFileLog(options, _clock);

        for (var i = 1; i <= 4; i++)
        {
            log.Write(LogKind.Capture, $"frames={i:00}");
        }

        Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(["frames=04"], LogReader.ReadAll(options).Select(e => e.Message));
    }

    [Fact]
    public void A_write_that_cannot_reach_the_disk_does_not_throw()
    {
        // A file where the directory should be: CreateDirectory fails, and the watch must carry on.
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "in the way");
        var log = new RotatingFileLog(new RotatingFileLogOptions(_directory), _clock);

        var exception = Record.Exception(() => log.Write(LogKind.Error, "nowhere to go"));

        Assert.Null(exception);
        File.Delete(_directory);
    }

    [Fact]
    public void Concurrent_writers_never_tear_a_line()
    {
        var options = new RotatingFileLogOptions(_directory) { MaxFileBytes = 2_000, MaxFiles = 4 };
        var log = new RotatingFileLog(options, _clock);

        Parallel.For(0, 200, i => log.Write(LogKind.Capture, $"writer={i:000} " + new string('x', 20)));

        var entries = LogReader.ReadAll(options);
        Assert.All(entries, e => Assert.Matches("^writer=[0-9]{3} x{20}$", e.Message));
        Assert.True(entries.Count > 0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Rejects_senseless_limits(int maxFiles, long maxBytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RotatingFileLog(new RotatingFileLogOptions(_directory) { MaxFiles = maxFiles, MaxFileBytes = maxBytes }, _clock));
}
