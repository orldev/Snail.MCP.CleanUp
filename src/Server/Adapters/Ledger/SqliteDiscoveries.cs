using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Discoveries as rows of the shared SQLite file, one per trace, counted every time a census sees it again.</summary>
public sealed class SqliteDiscoveries(LedgerFile file) : IDiscoveries
{
    /// <inheritdoc/>
    public async Task NoteAsync(IReadOnlyList<Discovery> discoveries, CancellationToken cancellationToken)
    {
        if (discoveries.Count == 0)
            return;

        await using var connection = await file.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var upsert = connection.Prepared(
            """
            INSERT INTO discoveries (trace_id, trail, key, title, bytes, owner, first_seen, last_seen, sightings)
            VALUES ($id, $trail, $key, $title, $bytes, $owner, $first, $last, 1)
            ON CONFLICT (trace_id) DO UPDATE SET
                last_seen = excluded.last_seen,
                sightings = discoveries.sightings + 1,
                bytes = COALESCE(excluded.bytes, discoveries.bytes);
            """,
            ("$id", null),
            ("$trail", null),
            ("$key", null),
            ("$title", null),
            ("$bytes", null),
            ("$owner", null),
            ("$first", null),
            ("$last", null));

        upsert.Transaction = transaction;

        foreach (var discovery in discoveries)
        {
            upsert.Parameters["$id"].Value = discovery.Trace.Id;
            upsert.Parameters["$trail"].Value = discovery.Trace.Trail;
            upsert.Parameters["$key"].Value = discovery.Trace.Key;
            upsert.Parameters["$title"].Value = discovery.Trace.Title;
            upsert.Parameters["$bytes"].Value = (object?)discovery.Trace.Bytes ?? DBNull.Value;
            upsert.Parameters["$owner"].Value = discovery.Owner.ToString();
            upsert.Parameters["$first"].Value = Moments.Write(discovery.FirstSeen);
            upsert.Parameters["$last"].Value = Moments.Write(discovery.LastSeen);

            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared(
            "SELECT trail, key, title, bytes, owner, first_seen, last_seen, sightings FROM discoveries ORDER BY first_seen DESC;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var discoveries = new List<Discovery>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var trace = new Trace(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3));

            discoveries.Add(new Discovery(
                trace,
                Owner.Parse(reader.GetString(4)),
                Moments.Read(reader.GetString(5)),
                Moments.Read(reader.GetString(6)),
                reader.GetInt32(7))
            {
                IsPresent = present.Contains(trace.Id)
            });
        }

        return discoveries;
    }

    /// <inheritdoc/>
    public async Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken)
    {
        var covered = (await ListAsync(new HashSet<string>(), cancellationToken)).Where(discovery => rule.Matches(discovery.Trace)).ToList();

        await using var connection = await file.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var delete = connection.Prepared("DELETE FROM discoveries WHERE trace_id = $id;", ("$id", null));

        delete.Transaction = transaction;

        foreach (var discovery in covered)
        {
            delete.Parameters["$id"].Value = discovery.Trace.Id;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return covered.Count;
    }
}
