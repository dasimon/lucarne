using System.Diagnostics;

namespace Lucarne.Core.Data;

/// <summary>How carrying the RemoteDeck data folder over to Lucarne's ended.</summary>
public enum MigrationResult
{
    /// <summary>There is no RemoteDeck folder: a new user, or one already carried over.</summary>
    NothingToDo,

    /// <summary>The RemoteDeck folder is now Lucarne's, whole; the old name no longer exists.</summary>
    Moved,

    /// <summary>Both folders exist (an earlier try of Lucarne made its own): neither was touched,
    /// and Lucarne keeps its own.</summary>
    BothExist,

    /// <summary>The RemoteDeck folder could not be moved, usually because a RemoteDeck still running
    /// holds a file in it. Nothing was changed: this session uses the RemoteDeck folder, and the
    /// next start tries again.</summary>
    Locked,
}

/// <summary>
/// Carries a RemoteDeck 0.9.0 data folder (<c>%APPDATA%\RemoteDeck</c>: the database with its
/// <c>-wal</c> and <c>-shm</c>, the settings, the logs) over to Lucarne's, so that a user who updates
/// finds their connections. One rename of the whole folder, never a copy and never a merge: a copy
/// leaves two databases that drift apart, a merge mixes two, and either can lose what the user
/// cannot see is missing. The credentials need nothing: DPAPI protects each with its own random
/// entropy, unrelated to the folder they are stored in.
/// </summary>
public static class LegacyDataFolder
{
    /// <summary>How long to wait between two attempts within the grace period.</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Renames <paramref name="legacyRoot"/> to <paramref name="newRoot"/> when the first exists and
    /// the second does not. Both are expected on one volume (they are siblings in
    /// <c>%APPDATA%</c>), where the rename is a single step that either happens whole or not at all.
    /// The folder the session then uses is <paramref name="newRoot"/>, except on
    /// <see cref="MigrationResult.Locked"/>: <paramref name="legacyRoot"/>.
    /// </summary>
    public static MigrationResult Migrate(string legacyRoot, string newRoot)
    {
        if (!Directory.Exists(legacyRoot)) return MigrationResult.NothingToDo;
        if (Directory.Exists(newRoot) || File.Exists(newRoot)) return MigrationResult.BothExist;

        try
        {
            Directory.Move(legacyRoot, newRoot);
            return MigrationResult.Moved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Measured: Windows refuses to rename a directory while any file inside it is open,
            // whatever the sharing it was opened with.
            return MigrationResult.Locked;
        }
    }

    /// <summary>
    /// <see cref="Migrate(string, string)"/>, tried again while the folder is
    /// <see cref="MigrationResult.Locked"/>, for at most <paramref name="grace"/>. The update case
    /// needs it: RemoteDeck 0.9.0 starts the updated executable from its <c>Exit</c> event, with its
    /// log still open, and lets go of it only when its process ends a moment later.
    /// </summary>
    public static MigrationResult Migrate(string legacyRoot, string newRoot, TimeSpan grace)
    {
        var start = Stopwatch.GetTimestamp();
        while (true)
        {
            var result = Migrate(legacyRoot, newRoot);
            if (result != MigrationResult.Locked || Stopwatch.GetElapsedTime(start) >= grace) return result;
            Thread.Sleep(RetryInterval);
        }
    }

    /// <summary>
    /// Deletes what RemoteDeck's updates left in <paramref name="legacyLocalRoot"/>
    /// (<c>%LOCALAPPDATA%\RemoteDeck</c>): its <c>update</c> folder, then the folder itself if that
    /// leaves it empty. Downloads only, nothing the user made, so it is not carried over. Never
    /// throws: a file still held is skipped, and goes at a later start.
    /// </summary>
    public static void RemoveUpdateFolder(string legacyLocalRoot)
    {
        try
        {
            Directory.Delete(Path.Combine(legacyLocalRoot, "update"), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, or a file still held.
        }

        try
        {
            // Not recursive: anything else in it is not ours, and the delete then fails.
            Directory.Delete(legacyLocalRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, or not empty.
        }
    }
}
