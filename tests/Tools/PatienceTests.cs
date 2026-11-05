using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using Snail.MCP.CleanUp.Tools;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tests.Tools;

public sealed class PatienceTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    private readonly TaskCompletionSource<CallToolResult> _stuck = new();

    private readonly Patience _patience;

    public PatienceTests() => _patience = new Patience(_clock, NullLogger<Patience>.Instance);

    /// <summary>A quick tool that never answers is answered for at its limit, not left to keep the session waiting.</summary>
    [Fact]
    public async Task QuickTool_StuckPastItsLimit_IsAnsweredAsOverdue()
    {
        var answering = _patience.AnswerAsync("cleanup_run", _ => _stuck.Task, CancellationToken.None);

        await AdvancedUntilAnsweredAsync(answering, Patience.Quick);

        var answer = await answering;

        Assert.True(answer.IsError);
        Assert.Equal(Messages.Overdue, answer.Error());
        Assert.Equal("overdue", answer.Type());
    }

    /// <summary>A removal of many things waits for something by design, and is waited for past the quick limit.</summary>
    [Fact]
    public async Task Apply_PastTheQuickLimit_IsStillWaitedFor()
    {
        var answering = _patience.AnswerAsync("cleanup_apply", _ => _stuck.Task, CancellationToken.None);

        _clock.Advance(Patience.Quick * 3);
        await Task.Delay(50);

        Assert.False(answering.IsCompleted);

        _stuck.SetResult(ToolAnswer.Ok(new JsonObject()));

        Assert.NotEqual(true, (await answering).IsError);
    }

    /// <summary>A call that blocks before its first await is still answered at its limit.</summary>
    [Fact]
    public async Task QuickTool_BlockedBeforeItsFirstAwait_IsAnsweredAsOverdue()
    {
        using var released = new ManualResetEventSlim();

        var answering = _patience.AnswerAsync("cleanup_diagnose", _ => Blocked(released), CancellationToken.None);

        await AdvancedUntilAnsweredAsync(answering, Patience.Quick);
        released.Set();

        Assert.True((await answering).IsError);
    }

    /// <summary>The time stated to the agent is the limit in code.</summary>
    [Fact]
    public void Overdue_StatesTheQuickLimit()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), Patience.Quick);
        Assert.Contains("within two minutes", Messages.Overdue, StringComparison.Ordinal);
    }

    private static Task<CallToolResult> Blocked(ManualResetEventSlim released)
    {
        released.Wait(TimeSpan.FromSeconds(30));

        return Task.FromResult(ToolAnswer.Ok(new JsonObject()));
    }

    /// <summary>Moves the clock past the limit until the call is answered: the wait arms its timer only once the call is running.</summary>
    private async Task AdvancedUntilAnsweredAsync(Task<CallToolResult> answering, TimeSpan limit)
    {
        for (var attempt = 0; attempt < 100 && !answering.IsCompleted; attempt++)
        {
            _clock.Advance(limit);
            await Task.Delay(20);
        }
    }
}
