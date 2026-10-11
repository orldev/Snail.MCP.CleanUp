using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.CleanUp.Adapters.Ledger;

/// <summary>Shown plans as JSON rows of the shared SQLite file, so another process of the same session can carry one out.</summary>
public sealed class SqlitePlans(LedgerFile file, TimeProvider clock) : IPlans
{
    /// <summary>How long a plan is kept after it can no longer be carried out, for whoever asks what happened to it.</summary>
    private static readonly TimeSpan Kept = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public async Task SaveAsync(Plan plan, CancellationToken cancellationToken)
    {
        await using var connection = await file.OpenAsync(cancellationToken);

        await connection.RunAsync(
            "INSERT OR REPLACE INTO plans (id, made_at, body) VALUES ($id, $at, $body);",
            cancellationToken,
            ("$id", plan.Id),
            ("$at", Moments.Write(plan.MadeAt)),
            ("$body", JsonSerializer.Serialize(plan, PlanJson.Default.Plan)));

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

        return await command.ExecuteScalarAsync(cancellationToken) is string body ? JsonSerializer.Deserialize(body, PlanJson.Default.Plan) : null;
    }
}

/// <summary>A plan as JSON, written by generated code instead of reflection.</summary>
/// <remarks>
/// Reflection also wrote <see cref="Plan.Removals"/>, a list computed from the decisions, so every stored plan
/// carried its removals twice — a third of the row, measured on 2026-10-12. Generated code leaves computed
/// properties out, reads the rows written before without complaint, and is what a native build can run at all.
/// </remarks>
[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Plan))]
internal sealed partial class PlanJson : JsonSerializerContext;
