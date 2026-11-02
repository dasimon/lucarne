using Lucarne.Core.Data;

namespace Lucarne.Core.Tests.Data;

/// <summary>The connection policy itself, apart from what any repository stores through it.</summary>
public sealed class SqliteDatabaseTests
{
    [Fact]
    public void A_closed_connection_lets_go_of_its_file_even_after_a_collection()
    {
        // A command collected undisposed keeps its statement until its finalizer runs, and SQLite keeps
        // the file open with it after the connection is closed: in a busy test run, the move of the
        // RemoteDeck folder failed that way. It is a race with the finalizer thread, which a first call
        // in a fresh process often wins: hence several.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var tmp = new TempDatabase();
            using (var c = tmp.Db.Open())
            {
                using var insert = c.Cmd("CREATE TABLE t (v TEXT); INSERT INTO t VALUES ('kept');");
                insert.ExecuteNonQuery();
                GC.Collect();
            }

            var moved = tmp.Path + ".moved";
            File.Move(tmp.Path, moved);
            File.Move(moved, tmp.Path);
        }
    }
}
