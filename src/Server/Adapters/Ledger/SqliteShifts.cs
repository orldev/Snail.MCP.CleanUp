namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Shifts as rows of the shared SQLite file: one row per duty, naming who holds it and until when.</summary>
/// <remarks>Taking a shift is one statement, so two servers asking at the same moment cannot both get it.</remarks>
public sealed class SqliteShifts(LedgerFile file, TimeProvider clock) : IShifts
{
    /// <inheritdoc/>
    public async Task<bool> TakeAsync(string duty, string watcher, TimeSpan length, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var connection = await file.OpenAsync(cancellationToken);

        return await connection.RunAsync(
            """
            INSERT INTO shifts (duty, watcher, until) VALUES ($duty, $watcher, $until)
            ON CONFLICT (duty) DO UPDATE SET watcher = excluded.watcher, until = excluded.until
            WHERE shifts.watcher = excluded.watcher OR shifts.until < $now;
            """,
            cancellationToken,
            ("$duty", duty),
            ("$watcher", watcher),
            ("$until", Moments.Write(now + length)),
            ("$now", Moments.Write(now))) > 0;
    }

    /// <inheritdoc/>
    public async Task LeaveAsync(string duty, string watcher, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync("DELETE FROM shifts WHERE duty = $duty AND watcher = $watcher;", cancellationToken, ("$duty", duty), ("$watcher", watcher));
    }
}
