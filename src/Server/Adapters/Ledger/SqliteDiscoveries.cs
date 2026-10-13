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
    /// <remarks>
    /// Whether a row is gone for good is decided before the row is read: <c>last_seen</c> is compared in the query, and
    /// a row too old whose thing is not there costs its id and nothing more. On the first census after a week of
    /// sessions that is most of the table.
    /// </remarks>
    public async Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var forgotten = Moments.Write(now - Discovery.Remembered);
        var discoveries = new List<Discovery>();
        var seen = new List<string>();

        await using var connection = await file.OpenAsync(cancellationToken);

        await using (var command = connection.Prepared(
            "SELECT trace_id, last_seen < $forgotten, trail, key, title, bytes, owner, first_seen, last_seen, sightings FROM discoveries ORDER BY first_seen DESC;",
            ("$forgotten", forgotten)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var isPresent = present.Contains(id);

                if (!isPresent && reader.GetBoolean(1))
                    continue;

                if (isPresent)
                    seen.Add(id);

                long? bytes = await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetInt64(5);

                discoveries.Add(new Discovery(
                    new Trace(reader.GetString(2), reader.GetString(3), reader.GetString(4), bytes),
                    Owner.Parse(reader.GetString(6)),
                    Moments.Read(reader.GetString(7)),
                    isPresent ? now : Moments.Read(reader.GetString(8)),
                    reader.GetInt32(9))
                {
                    IsPresent = isPresent
                });
            }
        }

        await KeepAsync(connection, seen, now, forgotten, cancellationToken);

        return discoveries;
    }

    /// <summary>Writes down that what is still there was seen at <paramref name="now"/>, then crosses off what has been gone too long.</summary>
    /// <remarks>In this order, so that a thing still there is never crossed off for a census that came late.</remarks>
    private static async Task KeepAsync(SqliteConnection connection, IReadOnlyList<string> seen, DateTimeOffset now, string forgotten, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var again = connection.Prepared("UPDATE discoveries SET last_seen = $now WHERE trace_id = $id;", ("$now", Moments.Write(now)), ("$id", null)))
        {
            again.Transaction = transaction;

            foreach (var id in seen)
            {
                again.Parameters["$id"].Value = id;
                await again.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var forget = connection.Prepared("DELETE FROM discoveries WHERE last_seen < $forgotten;", ("$forgotten", forgotten)))
        {
            forget.Transaction = transaction;
            await forget.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken)
    {
        var covered = await CoveredAsync(rule, cancellationToken);

        await using var connection = await file.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var delete = connection.Prepared("DELETE FROM discoveries WHERE trace_id = $id;", ("$id", null));

        delete.Transaction = transaction;

        foreach (var id in covered)
        {
            delete.Parameters["$id"].Value = id;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return covered.Count;
    }

    /// <summary>The ids of every discovery written down that a rule matches, still there or not.</summary>
    private async Task<List<string>> CoveredAsync(Rule rule, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT trace_id, trail, key, title FROM discoveries;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var covered = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            if (rule.Matches(new Trace(reader.GetString(1), reader.GetString(2), reader.GetString(3))))
                covered.Add(reader.GetString(0));
        }

        return covered;
    }
}
