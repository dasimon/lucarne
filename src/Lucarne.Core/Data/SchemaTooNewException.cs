namespace Lucarne.Core.Data;

/// <summary>The database was written by a newer Lucarne; opening it read-write could corrupt it.</summary>
public sealed class SchemaTooNewException(int found, int supported)
    : Exception($"The database schema is version {found}, but this build supports up to version {supported}. Update Lucarne.")
{
    public int Found { get; } = found;
    public int Supported { get; } = supported;
}
