using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Domain;

/// <summary>What the server writes into its ledger and decides its plans by, unchanged by the move onto Snail.Toolkit.Cleanup.</summary>
public sealed class MachineKindsTests
{
    /// <summary>The order the server took things apart in when its kinds were still named by a core of its own.</summary>
    [Fact]
    public void Order_IsTheOneTheServerAlwaysHad() =>
        Assert.Equal(
            [
                "process", "mcp", "forward", "emulator", "simulator", "avd", "container", "network", "volume", "image", "dangling", "buildcache",
                "buildhistory", "worktree", "editor", "testresults", "remains", "outside", "file"
            ],
            MachineKinds.Order.Listed);

    [Fact]
    public void KindNoOneListed_GoesAfterEveryKindOfThisMachine() =>
        Assert.Equal(MachineKinds.Order.Listed.Count, MachineKinds.Order.Rank("learned"));

    /// <summary>Ledgers written before the move name their sessions <c>session:&lt;id&gt;</c>; they must still read as the same owner.</summary>
    [Fact]
    public void Session_IsWrittenAndReadAsTheLedgerAlwaysHadIt()
    {
        Assert.Equal("session:abc", Owner.Session("abc").ToString());
        Assert.Equal(Owner.Session("abc"), Owner.Parse("session:abc"));
    }

    /// <summary>A kind reads the agent's process back from the holders a census hands it.</summary>
    [Fact]
    public void Session_IsAHolderOfItsOwner_ThatKeepsItsProcess()
    {
        Holder holder = new Session(Owner.Session("abc"), 42, IsAlive: true) { Directory = "/work" };

        var session = Assert.IsType<Session>(holder);

        Assert.Equal(("abc", 42, "/work"), (session.Id, session.Pid, holder.Directory));
        Assert.Equal(Owner.Session("abc"), holder.Owner);
    }
}
