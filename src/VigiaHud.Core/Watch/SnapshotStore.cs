using System.Globalization;

namespace VigiaHud.Core.Watch;

/// <summary>
/// The debug snapshots on disk: one JPEG every snapshot interval, named by its UTC instant so they sort by time,
/// and never more than <see cref="MaxFiles"/> of them. The phone has little free space (DECISIONS 003), so the
/// oldest go as soon as a new one arrives. Nothing here throws into the watch: a failed save is reported, not thrown.
/// </summary>
public sealed class SnapshotStore
{
    private const string Extension = ".jpg";
    private const string NameFormat = "yyyyMMdd'-'HHmmss'Z'";

    public SnapshotStore(string directory, int maxFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);
        Directory = directory;
        MaxFiles = maxFiles;
    }

    public string Directory { get; }

    public int MaxFiles { get; }

    /// <summary>Writes the JPEG and prunes the oldest ones. Returns the path, or null and the reason if it failed.</summary>
    public (string? Path, string? Error) Save(DateTimeOffset at, ReadOnlySpan<byte> jpeg)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = System.IO.Path.Combine(Directory, at.ToUniversalTime().ToString(NameFormat, CultureInfo.InvariantCulture) + Extension);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(jpeg);
            }

            Prune();
            return (path, null);
        }
        catch (Exception error)
        {
            return (null, error.Message);
        }
    }

    /// <summary>The snapshots on disk, oldest first.</summary>
    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.GetFiles(Directory, "*" + Extension)
            .OrderBy(System.IO.Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private void Prune()
    {
        var files = List();
        for (var i = 0; i < files.Count - MaxFiles; i++)
        {
            File.Delete(files[i]);
        }
    }
}
