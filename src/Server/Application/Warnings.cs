using System.Globalization;
using Snail.MCP.CleanUp.Adapters.Resources;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What an inventory and a plan say before anything else: what grows where nothing will remove it.</summary>
/// <remarks>
/// A running session's scratchpad is never released, so «blocked: its session is still running» was all the inventory
/// said of one that grew to 50 GB in a day (2026-10-13). Past a size it is named, with what the session itself could give
/// back of it.
/// </remarks>
public static class Warnings
{
    /// <summary>The warnings of an inventory: every running session's scratchpad of <paramref name="scratchpadBytes"/> or more.</summary>
    public static IReadOnlyList<string> Of(Inventory inventory, long scratchpadBytes)
    {
        var alive = inventory.Alive;
        var warnings = new List<string>();

        foreach (var scratchpad in inventory.Resources)
        {
            if (!scratchpad.Facts.TryGetValue(FileKind.SessionFact, out var session) || !alive.Contains(scratchpad.Owner) || (scratchpad.Bytes ?? 0) < scratchpadBytes)
                continue;

            var outputs = inventory.Resources
                .Where(resource => resource.Facts.TryGetValue(FileKind.ScratchpadFact, out var lies) && string.Equals(lies, scratchpad.Key, StringComparison.Ordinal) && resource.Blocker is null)
                .ToList();
            var offered = outputs.Count == 0
                ? "nothing in it is rebuilt cheaply and unused for two hours"
                : string.Create(CultureInfo.InvariantCulture, $"{outputs.Count} build output(s) of {Views.Size(outputs.Sum(output => output.Bytes ?? 0), outputs.Any(output => output.IsAtLeast))} in it can go with cleanup_plan mine of that session");

            warnings.Add($"the scratchpad of running session {session} holds {Views.Size(scratchpad.Bytes, scratchpad.IsAtLeast)} ({scratchpad.Key}); {offered}");
        }

        return warnings;
    }

    /// <summary>The bytes of a number of gigabytes, as the settings give them.</summary>
    public static long Bytes(double gigabytes) => (long)(gigabytes * 1e9);
}
