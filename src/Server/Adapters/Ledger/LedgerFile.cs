using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>The SQLite file the account lives in, opened with the settings several processes need to share it.</summary>
/// <remarks>
/// Every session on the machine starts its own server, and all of them write to this one file. Write-ahead
/// logging lets readers go on while one of them writes, and the busy timeout makes a writer wait its turn
/// instead of failing with «database is locked» the moment two sessions end at once. With the log, a commit
/// need not reach the disk before the call returns: a power cut can lose the last census's notes, never the
/// file, and the next census writes them again.
/// </remarks>
public sealed class LedgerFile(string path) : IDisposable
{
    private const string Schema =
        """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS holdings (
            resource_id TEXT PRIMARY KEY,
            owner TEXT NOT NULL,
            evidence INTEGER NOT NULL,
            recorded_at TEXT NOT NULL,
            title TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS kept (
            resource_id TEXT PRIMARY KEY,
            reason TEXT NOT NULL,
            kept_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS plans (
            id TEXT PRIMARY KEY,
            made_at TEXT NOT NULL,
            body TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS baselines (
            owner TEXT NOT NULL,
            trace_id TEXT NOT NULL,
            taken_at TEXT NOT NULL,
            PRIMARY KEY (owner, trace_id));
        CREATE TABLE IF NOT EXISTS discoveries (
            trace_id TEXT PRIMARY KEY,
            trail TEXT NOT NULL,
            key TEXT NOT NULL,
            title TEXT NOT NULL,
            bytes INTEGER NULL,
            at_least INTEGER NOT NULL DEFAULT 0,
            owner TEXT NOT NULL,
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL,
            sightings INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS shifts (
            duty TEXT PRIMARY KEY,
            watcher TEXT NOT NULL,
            until TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS disk (
            at TEXT PRIMARY KEY,
            free INTEGER NOT NULL,
            total INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS growth (
            resource_id TEXT NOT NULL,
            at TEXT NOT NULL,
            title TEXT NOT NULL,
            bytes INTEGER NOT NULL,
            PRIMARY KEY (resource_id, at));
        """;

    private readonly SemaphoreSlim _preparing = new(1, 1);

    private bool _isPrepared;

    /// <summary>Where the file is.</summary>
    public string Path { get; } = path;

    /// <summary>An open connection to the file, its schema in place.</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, DefaultTimeout = 30 }.ToString());

        await connection.OpenAsync(cancellationToken);
        await PrepareAsync(connection, cancellationToken);
        await connection.RunAsync("PRAGMA busy_timeout = 10000; PRAGMA synchronous = NORMAL;", cancellationToken);

        return connection;
    }

    private async Task PrepareAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_isPrepared)
            return;

        await _preparing.WaitAsync(cancellationToken);

        try
        {
            if (_isPrepared)
                return;

            await connection.RunAsync(Schema, cancellationToken);
            await AddedAsync(connection, "discoveries", "at_least INTEGER NOT NULL DEFAULT 0", cancellationToken);
            _isPrepared = true;
        }
        finally
        {
            _preparing.Release();
        }
    }

    /// <summary>A column a file written by an earlier version lacks, added in place; rows written before it take its default.</summary>
    /// <remarks>
    /// Servers of several versions share the file, so two may add the same column at once: the one that comes second is told
    /// the column is already there, which is what it wanted. A server of before writes its rows without the column and gets
    /// the default.
    /// </remarks>
    private static async Task AddedAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        var name = column[..column.IndexOf(' ', StringComparison.Ordinal)];

        await using var present = connection.Prepared($"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name;", ("$name", name));

        if (Convert.ToInt64(await present.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0)
            return;

        try
        {
            await connection.RunAsync($"ALTER TABLE {table} ADD COLUMN {column};", cancellationToken);
        }
        catch (SqliteException added) when (added.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _preparing.Dispose();
}

/// <summary>Running a statement with its parameters in one call.</summary>
internal static class Statements
{
    extension(SqliteConnection connection)
    {
        public async Task<int> RunAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
        {
            await using var command = connection.Prepared(sql, parameters);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public SqliteCommand Prepared(string sql, params (string Name, object? Value)[] parameters)
        {
            var command = connection.CreateCommand();

            command.CommandText = sql;

            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);

            return command;
        }
    }
}
