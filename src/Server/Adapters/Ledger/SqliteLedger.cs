using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>The ledger as rows of the shared SQLite file.</summary>
public sealed class SqliteLedger(LedgerFile file) : ILedger
{
    /// <inheritdoc/>
    /// <remarks>
    /// The condition on the update is what keeps a weaker sighting from overwriting a stronger one: a process
    /// recorded at birth by this server stays recorded so even when a later census sees it only as a child. It compares
    /// strengths, not codes: the environment's code is the newest, its strength between ancestry and a label.
    /// </remarks>
    public async Task RecordAsync(Holding holding, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync(
            """
            INSERT INTO holdings (resource_id, owner, evidence, recorded_at, title)
            VALUES ($id, $owner, $evidence, $at, $title)
            ON CONFLICT (resource_id) DO UPDATE SET
                owner = excluded.owner, evidence = excluded.evidence, recorded_at = excluded.recorded_at, title = excluded.title
            WHERE (CASE excluded.evidence WHEN 5 THEN 2.5 ELSE excluded.evidence END) >= (CASE holdings.evidence WHEN 5 THEN 2.5 ELSE holdings.evidence END);
            """,
            cancellationToken,
            ("$id", holding.ResourceId),
            ("$owner", holding.Owner.ToString()),
            ("$evidence", EvidenceCodes.Write(holding.Evidence)),
            ("$at", Moments.Write(holding.RecordedAt)),
            ("$title", holding.Title));
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(string resourceId, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync("DELETE FROM holdings WHERE resource_id = $id;", cancellationToken, ("$id", resourceId));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared(
            """
            SELECT h.resource_id, h.owner, h.evidence, h.recorded_at, h.title, k.reason
            FROM holdings h LEFT JOIN kept k ON k.resource_id = h.resource_id
            ORDER BY h.recorded_at;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var holdings = new List<Holding>();

        while (await reader.ReadAsync(cancellationToken))
        {
            string? keptBecause = await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetString(5);

            holdings.Add(new Holding(
                reader.GetString(0),
                Owner.Parse(reader.GetString(1)),
                EvidenceCodes.Read(reader.GetInt32(2)),
                Moments.Read(reader.GetString(3)),
                reader.GetString(4))
            {
                KeptBecause = keptBecause
            });
        }

        return holdings;
    }

    /// <inheritdoc/>
    public async Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync(
            "INSERT INTO kept (resource_id, reason, kept_at) VALUES ($id, $reason, $at) ON CONFLICT (resource_id) DO UPDATE SET reason = excluded.reason;",
            cancellationToken,
            ("$id", resourceId),
            ("$reason", reason),
            ("$at", Moments.Write(DateTimeOffset.UtcNow)));
    }

    /// <inheritdoc/>
    public async Task<bool> TryUnkeepAsync(string resourceId, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        return await connection.RunAsync("DELETE FROM kept WHERE resource_id = $id;", cancellationToken, ("$id", resourceId)) > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT resource_id, reason FROM kept;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var kept = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
            kept[reader.GetString(0)] = reader.GetString(1);

        return kept;
    }
}

/// <summary>Evidence as the codes it is stored under, which never change: a value added to <see cref="Evidence"/> takes a new code, whatever its place in the order.</summary>
internal static class EvidenceCodes
{
    /// <summary>The code of the environment, the newest; its strength is between ancestry's (2) and a label's (3).</summary>
    public const int Environment = 5;

    public static int Write(Evidence evidence) => evidence switch
    {
        Evidence.None => 0,
        Evidence.Appeared => 1,
        Evidence.Ancestry => 2,
        Evidence.Label => 3,
        Evidence.Ledger => 4,
        Evidence.Environment => Environment,
        _ => 0
    };

    public static Evidence Read(int code) => code switch
    {
        1 => Evidence.Appeared,
        2 => Evidence.Ancestry,
        3 => Evidence.Label,
        4 => Evidence.Ledger,
        Environment => Evidence.Environment,
        _ => Evidence.None
    };
}

/// <summary>Moments as the text they are stored in: round-trip ISO 8601, so they sort as they compare.</summary>
internal static class Moments
{
    public static string Write(DateTimeOffset moment) => moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Read(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
