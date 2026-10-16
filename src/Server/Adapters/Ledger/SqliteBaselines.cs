using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Baselines in the shared SQLite file: one row per owner, the hashes of the traces its session began with in one value.</summary>
/// <remarks>
/// A baseline is written once, when its session first takes part in a census, and never changed after; only the end
/// of the session removes it. So one read is kept for as long as the session runs.
/// <para>
/// A row per trace id was how it began, when a session began with two and a half thousand of them. On 2026-10-16 each
/// began with about 39 000, the table held 413 453 rows and took 115 MB of the ledger with its index, and every session
/// start wrote 39 000 rows and every end deleted them. The hashes the census reads anyway (see <see cref="Baseline"/>),
/// in one value, take eight bytes an id. Servers of before still write and read rows, so a baseline is looked for in both,
/// and the rows a file holds are moved into sets once, when a server of this version first opens it.
/// </para>
/// </remarks>
public sealed class SqliteBaselines(LedgerFile file) : IBaselines
{
    /// <summary>A trace every baseline holds, so that a session which began on an empty machine still has one.</summary>
    private const string Marker = "baseline:taken";

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

    /// <inheritdoc/>
    public async Task SaveAsync(Owner owner, IReadOnlyList<string> traceIds, DateTimeOffset takenAt, CancellationToken cancellationToken)
    {
        if (_read.ContainsKey(owner))
            return;

        await using var connection = await file.OpenAsync(cancellationToken);

        if (await IsTakenAsync(connection, owner, cancellationToken))
            return;

        var seen = new Baseline();

        foreach (var traceId in traceIds)
            seen.Add(traceId);

        seen.Add(Marker);

        await connection.RunAsync(
            """
            INSERT OR IGNORE INTO baseline_sets (owner, taken_at, hashes)
            SELECT $owner, $at, $hashes WHERE NOT EXISTS (SELECT 1 FROM baselines WHERE owner = $owner);
            """,
            cancellationToken,
            ("$owner", owner.ToString()),
            ("$at", Moments.Write(takenAt)),
            ("$hashes", seen.ToBytes()));
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
            await connection.RunAsync("DELETE FROM baseline_sets; DELETE FROM baselines;", cancellationToken);

            return;
        }

        var names = string.Join(", ", kept.Select((_, position) => $"$o{position}"));
        (string Name, object? Value)[] parameters = [.. kept.Select((owner, position) => ($"$o{position}", (object?)owner))];

        await connection.RunAsync($"DELETE FROM baseline_sets WHERE owner NOT IN ({names}); DELETE FROM baselines WHERE owner NOT IN ({names});", cancellationToken, parameters);
    }

    /// <summary>Moves the baselines a file keeps as a row per trace into one set per owner; how many trace ids were moved.</summary>
    /// <remarks>
    /// One transaction, taken for writing at once: two servers of this version opening the file together move the rows once,
    /// the second finding none.
    /// </remarks>
    internal static async Task<int> MoveRowsIntoSetsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var sets = await RowsAsync(connection, transaction, cancellationToken);
        var moved = sets.Values.Sum(set => set.Seen.Count);

        if (moved == 0)
            return 0;

        foreach (var (owner, (takenAt, seen)) in sets)
            await InsertedAsync(connection, transaction, owner, takenAt, seen, cancellationToken);

        await using (var removed = connection.Prepared("DELETE FROM baselines;"))
        {
            removed.Transaction = transaction;
            await removed.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return moved;
    }

    private async Task<Baseline?> ReadAsync(Owner owner, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var set = connection.Prepared("SELECT hashes FROM baseline_sets WHERE owner = $owner;", ("$owner", owner.ToString()));

        if (await set.ExecuteScalarAsync(cancellationToken) is byte[] hashes)
            return Baseline.FromBytes(hashes);

        await using var rows = connection.Prepared("SELECT trace_id FROM baselines WHERE owner = $owner;", ("$owner", owner.ToString()));
        await using var reader = await rows.ExecuteReaderAsync(cancellationToken);

        var seen = new Baseline();

        while (await reader.ReadAsync(cancellationToken))
            seen.Add(reader.GetString(0));

        return seen.Count > 0 ? seen : null;
    }

    private static async Task<bool> IsTakenAsync(SqliteConnection connection, Owner owner, CancellationToken cancellationToken)
    {
        await using var taken = connection.Prepared(
            "SELECT 1 WHERE EXISTS (SELECT 1 FROM baseline_sets WHERE owner = $owner) OR EXISTS (SELECT 1 FROM baselines WHERE owner = $owner);",
            ("$owner", owner.ToString()));

        return await taken.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<Dictionary<string, (string TakenAt, Baseline Seen)>> RowsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var sets = new Dictionary<string, (string TakenAt, Baseline Seen)>(StringComparer.Ordinal);

        await using var rows = connection.Prepared("SELECT owner, trace_id, taken_at FROM baselines;");

        rows.Transaction = transaction;

        await using var reader = await rows.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var owner = reader.GetString(0);

            if (!sets.TryGetValue(owner, out var set))
                sets[owner] = set = (reader.GetString(2), new Baseline());

            set.Seen.Add(reader.GetString(1));
        }

        return sets;
    }

    private static async Task InsertedAsync(SqliteConnection connection, SqliteTransaction transaction, string owner, string takenAt, Baseline seen, CancellationToken cancellationToken)
    {
        await using var insert = connection.Prepared(
            "INSERT OR IGNORE INTO baseline_sets (owner, taken_at, hashes) VALUES ($owner, $at, $hashes);",
            ("$owner", owner),
            ("$at", takenAt),
            ("$hashes", seen.ToBytes()));

        insert.Transaction = transaction;
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
