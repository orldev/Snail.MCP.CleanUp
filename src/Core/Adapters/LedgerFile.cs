using Microsoft.Data.Sqlite;

namespace Snail.MCP.CleanUp.Core.Adapters;

/// <summary>The SQLite file the account lives in, opened with the settings several processes need to share it.</summary>
/// <remarks>
/// Every session on the machine starts its own server, and all of them write to this one file. Write-ahead
/// logging lets readers go on while one of them writes, and the busy timeout makes a writer wait its turn
/// instead of failing with «database is locked» the moment two sessions end at once.
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
            owner TEXT NOT NULL,
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL,
            sightings INTEGER NOT NULL);
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
        await connection.RunAsync("PRAGMA busy_timeout = 10000;", cancellationToken);

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
            _isPrepared = true;
        }
        finally
        {
            _preparing.Release();
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
