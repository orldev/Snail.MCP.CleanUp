namespace Snail.MCP.CleanUp.Core.Adapters;

/// <summary>Baselines as rows of the shared SQLite file, one row per trace an owner's session began with.</summary>
public sealed class SqliteBaselines(LedgerFile file) : IBaselines
{
    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>?> FindAsync(Owner owner, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT trace_id FROM baselines WHERE owner = $owner;", ("$owner", owner.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
            seen.Add(reader.GetString(0));

        return seen.Count > 0 ? seen : null;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Owner owner, IReadOnlyList<string> traceIds, DateTimeOffset takenAt, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var taken = connection.Prepared("SELECT 1 FROM baselines WHERE owner = $owner LIMIT 1;", ("$owner", owner.ToString())))
        {
            taken.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

            if (await taken.ExecuteScalarAsync(cancellationToken) is not null)
                return;
        }

        await using var insert = connection.Prepared(
            "INSERT OR IGNORE INTO baselines (owner, trace_id, taken_at) VALUES ($owner, $trace, $at);",
            ("$owner", owner.ToString()),
            ("$trace", string.Empty),
            ("$at", Moments.Write(takenAt)));

        insert.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;

        foreach (var traceId in traceIds.Append(Marker))
        {
            insert.Parameters["$trace"].Value = traceId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task KeepOnlyAsync(IReadOnlySet<Owner> owners, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        var kept = owners.Select(owner => owner.ToString()).ToList();

        if (kept.Count == 0)
        {
            await connection.RunAsync("DELETE FROM baselines;", cancellationToken);

            return;
        }

        var names = string.Join(", ", kept.Select((_, position) => $"$o{position}"));

        await connection.RunAsync(
            $"DELETE FROM baselines WHERE owner NOT IN ({names});",
            cancellationToken,
            [.. kept.Select((owner, position) => ($"$o{position}", (object?)owner))]);
    }

    /// <summary>A row every baseline has, so that a session which began on an empty machine still has one.</summary>
    private const string Marker = "baseline:taken";
}
