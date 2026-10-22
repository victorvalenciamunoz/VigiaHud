using VigiaHud.Core.Watch;

namespace VigiaHud.Core.Tests.Watch;

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vigiahud-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Saves_named_by_utc_instant_and_creates_the_directory()
    {
        var store = new SnapshotStore(_directory, 10);

        var (path, error) = store.Save(new DateTimeOffset(2026, 10, 22, 16, 5, 9, TimeSpan.FromHours(2)), [1, 2, 3]);

        Assert.Null(error);
        Assert.Equal(Path.Combine(_directory, "20261022-140509Z.jpg"), path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path!));
    }

    [Fact]
    public void Keeps_only_the_newest_max_files()
    {
        var store = new SnapshotStore(_directory, 3);

        for (var i = 0; i < 5; i++)
        {
            store.Save(Start + TimeSpan.FromMinutes(10 * i), [(byte)i]);
        }

        Assert.Equal(
            ["20261022-142000Z.jpg", "20261022-143000Z.jpg", "20261022-144000Z.jpg"],
            store.List().Select(Path.GetFileName));
    }

    [Fact]
    public void Lists_nothing_before_the_first_save() =>
        Assert.Empty(new SnapshotStore(_directory, 3).List());

    [Fact]
    public void A_failed_save_is_reported_not_thrown()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "in the way");
        var store = new SnapshotStore(_directory, 3);

        var (path, error) = store.Save(Start, [1]);

        Assert.Null(path);
        Assert.NotNull(error);
        File.Delete(_directory);
    }

    [Fact]
    public void Rejects_a_senseless_limit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotStore(_directory, 0));
}
