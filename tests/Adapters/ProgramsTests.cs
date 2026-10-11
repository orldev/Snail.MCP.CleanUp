using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class ProgramsTests
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    private readonly Performance _performance;

    private readonly Programs _programs;

    public ProgramsTests()
    {
        _performance = new Performance(_clock);
        _programs = new Programs(_performance, _clock);
    }

    /// <summary>A census asks whether Docker runs from eight kinds at once; one answer serves all of them.</summary>
    [Fact]
    public async Task Readings_OfTheSameQuestionAtOnce_RunTheProgramOnce()
    {
        var readings = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _programs.ReadAsync("/bin/echo", ["shared"], Freshness, Freshness, CancellationToken.None)));

        Assert.All(readings, reading => Assert.Equal("shared\n", reading.Output));
        Assert.Equal(1, _performance.Runs("echo shared"));
    }

    [Fact]
    public async Task Reading_OlderThanItsFreshness_IsTakenAgain()
    {
        await _programs.ReadAsync("/bin/echo", ["again"], Freshness, Freshness, CancellationToken.None);
        _clock.Advance(Freshness + TimeSpan.FromSeconds(1));
        await _programs.ReadAsync("/bin/echo", ["again"], Freshness, Freshness, CancellationToken.None);

        Assert.Equal(2, _performance.Runs("echo again"));
    }

    /// <summary>A command that changes something, or checks right before it does, never gets an answer read a moment ago.</summary>
    [Fact]
    public async Task Run_IsNeverShared()
    {
        await _programs.RunAsync("/bin/echo", ["fresh"], Freshness, CancellationToken.None);
        await _programs.RunAsync("/bin/echo", ["fresh"], Freshness, CancellationToken.None);

        Assert.Equal(2, _performance.Runs("echo fresh"));
    }
}
