using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Machine;

[Collection(OnThisMac.Name)]
public sealed class SessionsOnThisMacTests
{
    private static MachineSessions Sessions => new(new ServerConfig(), new ProcessTable(new Programs(), new FakeTimeProvider(DateTimeOffset.UtcNow)));

    /// <summary>Run from a Claude Code session, the test host has an agent above it, and that agent is a session.</summary>
    [MachineFact]
    public async Task TestRun_StartedFromAnAgent_BelongsToItsSession()
    {
        var current = await Sessions.CurrentAsync(CancellationToken.None);
        var sessions = await Sessions.ListAsync(CancellationToken.None);

        Assert.True(current.IsKnown);
        Assert.Contains(sessions, session => session.IsAlive && session.Owner == current);
        Assert.False(current.Id.StartsWith(MachineSessions.ByProcess, StringComparison.Ordinal), "SleepOf knows this session, so it is named by SleepOf's id");
    }
}
