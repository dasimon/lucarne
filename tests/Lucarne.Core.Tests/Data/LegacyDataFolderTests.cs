using Lucarne.Core.Data;
using Microsoft.Data.Sqlite;

namespace Lucarne.Core.Tests.Data;

/// <summary>
/// Carrying a RemoteDeck 0.9.0 data folder over to Lucarne's, on throw-away folders: the user's real
/// <c>%APPDATA%</c> is never touched here.
/// </summary>
public sealed class LegacyDataFolderTests : IDisposable
{
    /// <summary>What 0.9.0 leaves in its folder, with distinct contents so a mix-up shows.</summary>
    private static readonly string[] DataFiles =
    [
        "connections.db",
        "connections.db-wal",
        "connections.db-shm",
        "settings.json",
        Path.Combine("logs", "probe-l0.log"),
        Path.Combine("logs", "probe-l0-apply-update.log"),
    ];

    private readonly string _parent = Path.Combine(Path.GetTempPath(), $"lucarne-legacy-{Guid.NewGuid():N}");
    private string Legacy => Path.Combine(_parent, "RemoteDeck");
    private string New => Path.Combine(_parent, "Lucarne");

    public LegacyDataFolderTests() => Directory.CreateDirectory(_parent);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                // A file just closed can be held a moment longer by an antivirus scan.
                Thread.Sleep(50);
            }
        }
    }

    [Fact]
    public void Without_a_legacy_folder_there_is_nothing_to_do()
    {
        Assert.Equal(MigrationResult.NothingToDo, LegacyDataFolder.Migrate(Legacy, New));

        Assert.False(Directory.Exists(New), "Nothing to move must not create the new folder.");
    }

    [Fact]
    public void Without_a_legacy_folder_the_new_one_is_left_alone()
    {
        Fill(New, "lucarne");

        Assert.Equal(MigrationResult.NothingToDo, LegacyDataFolder.Migrate(Legacy, New));

        AssertHolds(New, "lucarne");
    }

    [Fact]
    public void The_legacy_folder_alone_is_moved_whole()
    {
        Fill(Legacy, "remotedeck");

        Assert.Equal(MigrationResult.Moved, LegacyDataFolder.Migrate(Legacy, New));

        AssertHolds(New, "remotedeck");
        Assert.False(Directory.Exists(Legacy), "The legacy folder must be gone once moved.");
    }

    [Fact]
    public void A_moved_database_opens_with_its_rows()
    {
        // The WAL is part of the database: a row committed but not yet checkpointed lives there.
        Directory.CreateDirectory(Legacy);
        var legacyDb = new SqliteDatabase(Path.Combine(Legacy, "connections.db"));
        using (var connection = legacyDb.Open())
        {
            connection.Cmd("CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('kept');").ExecuteNonQuery();
        }

        Assert.Equal(MigrationResult.Moved, LegacyDataFolder.Migrate(Legacy, New));

        using var moved = new SqliteDatabase(Path.Combine(New, "connections.db")).Open();
        Assert.Equal("kept", moved.Cmd("SELECT v FROM t").ExecuteScalar());
    }

    [Fact]
    public void When_both_folders_exist_neither_is_touched()
    {
        // An earlier try of Lucarne left its own folder: merging would mix two databases, and
        // overwriting would lose one. Lucarne keeps its own; RemoteDeck's stays where it was.
        Fill(Legacy, "remotedeck");
        Fill(New, "lucarne");

        Assert.Equal(MigrationResult.BothExist, LegacyDataFolder.Migrate(Legacy, New));

        AssertHolds(Legacy, "remotedeck");
        AssertHolds(New, "lucarne");
    }

    [Fact]
    public void A_file_held_open_exclusively_leaves_the_legacy_folder_intact()
    {
        Fill(Legacy, "remotedeck");

        using (new FileStream(Path.Combine(Legacy, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(MigrationResult.Locked, LegacyDataFolder.Migrate(Legacy, New));
        }

        AssertHolds(Legacy, "remotedeck");
        Assert.False(Directory.Exists(New), "A failed move must not leave a partial new folder.");
    }

    [Fact]
    public void A_log_held_open_the_way_RemoteDeck_holds_it_also_locks_the_folder()
    {
        // 0.9.0 keeps its log open, shared for reading, writing and deletion, until the process
        // ends — and it starts the updated executable from its Exit event, before it has ended.
        // Measured: Windows refuses to rename a directory with any file open inside, delete
        // sharing or not.
        Fill(Legacy, "remotedeck");

        using (new FileStream(Path.Combine(Legacy, "logs", "probe-l0.log"), FileMode.Append, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            Assert.Equal(MigrationResult.Locked, LegacyDataFolder.Migrate(Legacy, New));
        }

        AssertHolds(Legacy, "remotedeck");
    }

    [Fact]
    public void A_database_open_in_another_RemoteDeck_locks_the_folder()
    {
        // A 0.9.0 installed elsewhere and still running.
        Directory.CreateDirectory(Legacy);
        var legacyDb = new SqliteDatabase(Path.Combine(Legacy, "connections.db"));
        using (var connection = legacyDb.Open())
        {
            connection.Cmd("CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('kept');").ExecuteNonQuery();
            Assert.Equal(MigrationResult.Locked, LegacyDataFolder.Migrate(Legacy, New));
        }

        Assert.False(Directory.Exists(New));
        using var still = legacyDb.Open();
        Assert.Equal("kept", still.Cmd("SELECT v FROM t").ExecuteScalar());
    }

    [Fact]
    public void A_lock_released_within_the_grace_period_still_ends_in_a_move()
    {
        // The update case: the 0.9.0 that started this process lets go of its log a moment later.
        Fill(Legacy, "remotedeck");
        var log = new FileStream(Path.Combine(Legacy, "logs", "probe-l0.log"), FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        using var release = new Timer(_ => log.Dispose(), null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

        var result = LegacyDataFolder.Migrate(Legacy, New, grace: TimeSpan.FromSeconds(5));

        log.Dispose();
        Assert.Equal(MigrationResult.Moved, result);
        AssertHolds(New, "remotedeck");
    }

    [Fact]
    public void A_lock_held_past_the_grace_period_is_reported_as_locked()
    {
        Fill(Legacy, "remotedeck");

        using (new FileStream(Path.Combine(Legacy, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(MigrationResult.Locked, LegacyDataFolder.Migrate(Legacy, New, grace: TimeSpan.FromMilliseconds(400)));
        }

        AssertHolds(Legacy, "remotedeck");
    }

    [Fact]
    public void The_legacy_update_folder_and_its_empty_parent_are_removed()
    {
        var local = Path.Combine(_parent, "Local", "RemoteDeck");
        Directory.CreateDirectory(Path.Combine(local, "update", "0.9.1"));
        File.WriteAllText(Path.Combine(local, "update", "0.9.1", "RemoteDeck.exe"), "exe");

        LegacyDataFolder.RemoveUpdateFolder(local);

        Assert.False(Directory.Exists(local));
    }

    [Fact]
    public void A_legacy_local_folder_holding_anything_else_is_kept()
    {
        var local = Path.Combine(_parent, "Local", "RemoteDeck");
        Directory.CreateDirectory(Path.Combine(local, "update"));
        File.WriteAllText(Path.Combine(local, "notes.txt"), "not ours to delete");

        LegacyDataFolder.RemoveUpdateFolder(local);

        Assert.False(Directory.Exists(Path.Combine(local, "update")));
        Assert.True(File.Exists(Path.Combine(local, "notes.txt")));
    }

    [Fact]
    public void Removing_the_legacy_update_folder_never_throws()
    {
        var local = Path.Combine(_parent, "Local", "RemoteDeck");
        LegacyDataFolder.RemoveUpdateFolder(local);

        Directory.CreateDirectory(Path.Combine(local, "update"));
        var held = Path.Combine(local, "update", "RemoteDeck.exe");
        File.WriteAllText(held, "exe");
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            LegacyDataFolder.RemoveUpdateFolder(local);
        }

        Assert.True(File.Exists(held), "A held file is skipped; it goes at a later start.");
    }

    private static void Fill(string root, string tag)
    {
        foreach (var file in DataFiles)
        {
            var path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{tag}:{file}");
        }
    }

    private static void AssertHolds(string root, string tag)
    {
        foreach (var file in DataFiles)
        {
            var path = Path.Combine(root, file);
            Assert.True(File.Exists(path), $"{path} is missing.");
            Assert.Equal($"{tag}:{file}", File.ReadAllText(path));
        }
    }
}
