using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

/// <summary>The scratchpad of 2026-10-13: 50 GB in a day, and the inventory said only that its session ran.</summary>
public sealed class WarningsTests
{
    private static Resource Scratchpad(Owner owner, long bytes) => Of(Kinds.File, $"/private/tmp/claude-501/p/{owner.Id}", owner, Evidence.Label) with
    {
        Bytes = bytes,
        Blocker = "its session is still running and writes into it",
        Facts = new Dictionary<string, string> { [FileKind.SessionFact] = owner.Id }
    };

    private static Resource Output(Owner owner, string name, long bytes, string? blocker = null) => Of(Kinds.File, $"/private/tmp/claude-501/p/{owner.Id}/{name}", owner, Evidence.Label) with
    {
        Bytes = bytes,
        Blocker = blocker,
        Facts = new Dictionary<string, string> { [FileKind.OutputFact] = "obj", [FileKind.ScratchpadFact] = $"/private/tmp/claude-501/p/{owner.Id}" }
    };

    [Fact]
    public void Scratchpad_OfARunningSession_PastTheSize_IsNamed_WithWhatItsSessionCanGiveBack()
    {
        var inventory = Inventory([Scratchpad(Neighbour, 50_200_000_000), Output(Neighbour, "a/obj", 3_000_000_000), Output(Neighbour, "b/obj", 1_000_000_000, "written 3 minutes ago")]);

        var warning = Assert.Single(Warnings.Of(inventory, Warnings.Bytes(10)));

        Assert.Contains("running session neighbour holds 50.2 GB", warning, StringComparison.Ordinal);
        Assert.Contains("1 build output(s) of 3 GB", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Scratchpad_BelowTheSize_OrOfAnEndedSession_IsNotNamed()
    {
        var inventory = Inventory([Scratchpad(Me, 9_000_000_000), Scratchpad(Gone, 80_000_000_000)]);

        Assert.Empty(Warnings.Of(inventory, Warnings.Bytes(10)));
        Assert.Single(Warnings.Of(inventory, Warnings.Bytes(5)));
    }
}
