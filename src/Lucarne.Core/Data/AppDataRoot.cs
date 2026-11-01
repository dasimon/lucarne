namespace Lucarne.Core.Data;

/// <summary>
/// The one place that says where Lucarne keeps its data: <c>%APPDATA%\Lucarne</c>, holding the
/// database, the settings and the logs. Every path to them is built from <see cref="Path"/>, so
/// that the folder is decided once, after the RemoteDeck folder has been carried over
/// (<see cref="LegacyDataFolder"/>) and before anything has been read or written in it.
/// </summary>
/// <remarks>
/// The application calls <see cref="Resolve"/> first thing at startup, in every mode. Reading
/// <see cref="Path"/> before that resolves it too, without waiting, so that a path read too early
/// can never create <c>%APPDATA%\Lucarne</c> beside an unmoved RemoteDeck folder — which would turn
/// every later start into <see cref="MigrationResult.BothExist"/>, with the user's connections left
/// behind.
/// </remarks>
public static class AppDataRoot
{
    private static readonly string ApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>RemoteDeck's folder, up to 0.9.0: <c>%APPDATA%\RemoteDeck</c>.</summary>
    public static string LegacyPath { get; } = System.IO.Path.Combine(ApplicationData, "RemoteDeck");

    /// <summary>Lucarne's folder: <c>%APPDATA%\Lucarne</c>.</summary>
    public static string NewPath { get; } = System.IO.Path.Combine(ApplicationData, "Lucarne");

    private static readonly object Gate = new();
    private static string? _path;
    private static MigrationResult _migration;

    /// <summary>The folder this session uses: <see cref="NewPath"/>, or <see cref="LegacyPath"/>
    /// while the RemoteDeck folder could not be moved.</summary>
    public static string Path
    {
        get
        {
            lock (Gate)
            {
                Resolve(TimeSpan.Zero);
                return _path!;
            }
        }
    }

    /// <summary>
    /// Carries the RemoteDeck folder over, waiting up to <paramref name="grace"/> for a RemoteDeck
    /// that is exiting to let go of it, and decides <see cref="Path"/>. Runs once per process: a later
    /// call returns the first one's result without touching the disk again.
    /// </summary>
    public static MigrationResult Resolve(TimeSpan grace)
    {
        lock (Gate)
        {
            if (_path is null)
            {
                _migration = LegacyDataFolder.Migrate(LegacyPath, NewPath, grace);
                _path = _migration == MigrationResult.Locked ? LegacyPath : NewPath;
            }

            return _migration;
        }
    }
}
