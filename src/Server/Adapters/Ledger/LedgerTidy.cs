using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Keeps the ledger file the size of what it holds: plans past keeping go, and the file gives its free pages back once they are many.</summary>
/// <remarks>
/// A plan past <see cref="SqlitePlans.Kept"/> was crossed off only when the next plan was saved, so the last ones stayed for as
/// long as nobody planned again: three plans of 17 MB each, made on 2026-11-06, were 51 MB of a file of 89 MB on 2026-11-12.
/// And SQLite keeps the pages of what it deleted for its next writes rather than give them back, so the file stays the size
/// of its busiest day. The watch's upkeep tidies both; a file another server holds busy is compacted another time.
/// </remarks>
public sealed class LedgerTidy(LedgerFile file, TimeProvider clock, long mostFree)
{
    /// <summary>How many free bytes make the file worth compacting, when they are also a quarter of it.</summary>
    public const long MostFree = 16 * 1024 * 1024;

    /// <summary>The tidying of the server's ledger, compacted past <see cref="MostFree"/>.</summary>
    public LedgerTidy(LedgerFile file, TimeProvider clock)
        : this(file, clock, MostFree)
    {
    }

    /// <summary>Crosses off the plans past keeping and compacts the file when it holds many free pages; what it did.</summary>
    public async Task<Tidied> TidyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        var forgotten = await connection.RunAsync(
            "DELETE FROM plans WHERE made_at < $before;",
            cancellationToken,
            ("$before", Moments.Write(clock.GetUtcNow() - SqlitePlans.Kept)));

        var pageSize = await ScalarAsync(connection, "PRAGMA page_size;", cancellationToken);
        var free = await ScalarAsync(connection, "PRAGMA freelist_count;", cancellationToken) * pageSize;
        var whole = await ScalarAsync(connection, "PRAGMA page_count;", cancellationToken) * pageSize;

        if (free < mostFree || free * 4 < whole)
            return new Tidied(forgotten, 0);

        try
        {
            await connection.RunAsync("VACUUM;", cancellationToken);
        }
        catch (SqliteException busy) when (busy.SqliteErrorCode is 5 or 6)
        {
            return new Tidied(forgotten, 0);
        }

        return new Tidied(forgotten, free);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.Prepared(sql);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value is long number ? number : 0;
    }
}

/// <summary>What a tidying of the ledger did.</summary>
/// <param name="Plans">How many plans past keeping it crossed off.</param>
/// <param name="Bytes">How many free bytes the file gave back; zero when it was not compacted.</param>
public sealed record Tidied(int Plans, long Bytes);
