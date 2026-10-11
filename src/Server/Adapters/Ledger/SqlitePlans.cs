using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Shown plans as JSON rows of the shared SQLite file, so another process of the same session can carry one out.</summary>
public sealed class SqlitePlans(LedgerFile file, TimeProvider clock) : IPlans
{
    /// <summary>How long a plan is kept after it can no longer be carried out, for whoever asks what happened to it.</summary>
    private static readonly TimeSpan Kept = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Options = new()
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc/>
    public async Task SaveAsync(Plan plan, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync(
            "INSERT OR REPLACE INTO plans (id, made_at, body) VALUES ($id, $at, $body);",
            cancellationToken,
            ("$id", plan.Id),
            ("$at", Moments.Write(plan.MadeAt)),
            ("$body", JsonSerializer.Serialize(plan, Options)));

        await connection.RunAsync(
            "DELETE FROM plans WHERE made_at < $before;",
            cancellationToken,
            ("$before", Moments.Write(clock.GetUtcNow() - Kept)));
    }

    /// <inheritdoc/>
    public async Task<Plan?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);
        await using var command = connection.Prepared("SELECT body FROM plans WHERE id = $id;", ("$id", id));

        return await command.ExecuteScalarAsync(cancellationToken) is string body ? JsonSerializer.Deserialize<Plan>(body, Options) : null;
    }
}
