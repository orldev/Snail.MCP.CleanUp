using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>The disk's history as rows of the shared SQLite file: free space every ten minutes for two weeks, sizes every hour for two days.</summary>
/// <remarks>
/// Every census writes, and a census comes every few seconds when the machine is busy; a row per census would be a
/// hundred thousand rows a week to answer «how much fell today». So a census overwrites the row of its ten minutes — or of
/// its hour, for sizes — and what is older than it is kept is dropped as it writes: at most 2 016 rows of free space and,
/// for thirty growers, 1 440 of sizes.
/// </remarks>
public sealed class SqliteDiskHistory(LedgerFile file) : IDiskHistory
{
    /// <summary>How long free space is kept.</summary>
    public static TimeSpan KeptFree { get; } = TimeSpan.FromDays(14);

    /// <summary>How long sizes are kept: a growth is measured over a day.</summary>
    public static TimeSpan KeptSizes { get; } = TimeSpan.FromDays(2);

    private static readonly TimeSpan FreeStep = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SizeStep = TimeSpan.FromHours(1);

    /// <inheritdoc/>
    public async Task RecordAsync(DiskSample disk, IReadOnlyList<SizeSample> sizes, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var free = connection.Prepared(
            "INSERT OR REPLACE INTO disk (at, free, total) VALUES ($at, $free, $total); DELETE FROM disk WHERE at < $before;",
            ("$at", Moments.Write(Step(disk.At, FreeStep))),
            ("$free", disk.Free),
            ("$total", disk.Total),
            ("$before", Moments.Write(disk.At - KeptFree)));

        free.Transaction = transaction;
        await free.ExecuteNonQueryAsync(cancellationToken);

        await using var size = connection.Prepared(
            "INSERT OR REPLACE INTO growth (resource_id, at, title, bytes) VALUES ($id, $at, $title, $bytes);",
            ("$id", string.Empty),
            ("$at", string.Empty),
            ("$title", string.Empty),
            ("$bytes", 0L));

        size.Transaction = transaction;

        foreach (var sample in sizes)
        {
            size.Parameters["$id"].Value = sample.ResourceId;
            size.Parameters["$at"].Value = Moments.Write(Step(sample.At, SizeStep));
            size.Parameters["$title"].Value = sample.Title;
            size.Parameters["$bytes"].Value = sample.Bytes;
            await size.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var dropped = connection.Prepared("DELETE FROM growth WHERE at < $before;", ("$before", Moments.Write(disk.At - KeptSizes)));

        dropped.Transaction = transaction;
        await dropped.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiskSample>> FreeSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT at, free, total FROM disk WHERE at >= $since ORDER BY at;", ("$since", Moments.Write(since)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var samples = new List<DiskSample>();

        while (await reader.ReadAsync(cancellationToken))
            samples.Add(new DiskSample(Moments.Read(reader.GetString(0)), reader.GetInt64(1), reader.GetInt64(2)));

        return samples;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SizeSample>> SizesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT at, resource_id, title, bytes FROM growth WHERE at >= $since ORDER BY at;", ("$since", Moments.Write(since)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var samples = new List<SizeSample>();

        while (await reader.ReadAsync(cancellationToken))
            samples.Add(new SizeSample(Moments.Read(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));

        return samples;
    }

    /// <summary>The start of the step a moment falls in.</summary>
    private static DateTimeOffset Step(DateTimeOffset at, TimeSpan step)
    {
        var utc = at.ToUniversalTime();

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % step.Ticks), TimeSpan.Zero);
    }
}
