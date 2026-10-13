using System.Collections.Concurrent;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Baselines as rows of the shared SQLite file, one row per trace an owner's session began with.</summary>
/// <remarks>
/// A baseline is written once, when its session first takes part in a census, and never changed after; only the end
/// of the session removes it. So one read is kept for as long as the session runs, instead of reading its
/// two and a half thousand rows back at every census. What is kept are the hashes of the ids, not the ids: see
/// <see cref="Baseline"/> for what that costs.
/// </remarks>
public sealed class SqliteBaselines(LedgerFile file) : IBaselines
{
    private readonly ConcurrentDictionary<Owner, Baseline> _read = new();

    /// <inheritdoc/>
    public async Task<Baseline?> FindAsync(Owner owner, CancellationToken cancellationToken)
    {
        if (_read.TryGetValue(owner, out var known))
            return known;

        var found = await ReadAsync(owner, cancellationToken);

        if (found is not null)
            _read[owner] = found;

        return found;
    }

    private async Task<Baseline?> ReadAsync(Owner owner, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT trace_id FROM baselines WHERE owner = $owner;", ("$owner", owner.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var seen = new Baseline();

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

        foreach (var owner in _read.Keys)
        {
            if (!owners.Contains(owner))
                _read.TryRemove(owner, out _);
        }

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
