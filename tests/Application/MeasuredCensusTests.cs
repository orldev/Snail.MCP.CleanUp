using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

/// <summary>
/// The watch, every session's hook and every tool call took a census of their own, each walking tens of thousands of
/// entries for 40 to 84 MB (2026-10-16), all at once when sessions start together.
/// </summary>
public sealed class MeasuredCensusTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly HeldKind _kind = new();

    private readonly LedgerFile _file;

    public MeasuredCensusTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    private MeasuredCensus CensusOver(params Session[] sessions)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
        var census = new Census([_kind], [], new FakeSessions(Me, sessions), new NoLedger(), new JsonRules(_scratch.File("rules.json"), []), new SqliteBaselines(_file), new SqliteDiscoveries(_file), clock);

        return new MeasuredCensus(census, new Performance(clock));
    }

    [Fact]
    public async Task Census_AskedForWhileOneIsUnderWay_JoinsIt_AndEachAskerIsItsOwn()
    {
        var census = CensusOver(Alive(Me), Alive(Neighbour));

        var mine = census.TakeAsync(Me, CancellationToken.None);
        var theirs = census.TakeAsync(Neighbour, CancellationToken.None);

        _kind.Let();

        var inventories = await Task.WhenAll(mine, theirs);

        Assert.Equal(1, _kind.Looks);
        Assert.Equal([Me, Neighbour], inventories.Select(inventory => inventory.Asker));
    }

    /// <summary>A session starting now is not among the holders of a census begun before it: its baseline is taken by one of its own.</summary>
    [Fact]
    public async Task Census_UnderWayBeforeItsAskerWasKnown_IsNotTheAskers()
    {
        var census = CensusOver(Alive(Me));

        var mine = census.TakeAsync(Me, CancellationToken.None);
        var newcomers = census.TakeAsync(Owner.Session("newcomer"), CancellationToken.None);

        _kind.Let();
        await Task.WhenAll(mine, newcomers);

        Assert.Equal(2, _kind.Looks);
    }

    [Fact]
    public async Task Census_CalledOffByWhoeverBeganIt_IsTakenAgainForTheOthers()
    {
        var census = CensusOver(Alive(Me), Alive(Neighbour));
        using var calledOff = new CancellationTokenSource();

        var mine = census.TakeAsync(Me, calledOff.Token);
        var theirs = census.TakeAsync(Neighbour, CancellationToken.None);

        await calledOff.CancelAsync();
        _kind.Let();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mine);
        Assert.Equal(Neighbour, (await theirs).Asker);
    }

    /// <summary>A kind that looks only when the test lets it, and counts its looks.</summary>
    private sealed class HeldKind : IResourceKind
    {
        private readonly TaskCompletionSource _let = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _looks;

        public string Name => Kinds.Process;

        public int Looks => Volatile.Read(ref _looks);

        public void Let() => _let.TrySetResult();

        public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _looks);
            await _let.Task.WaitAsync(cancellationToken);

            return new Observation(Name, [], []);
        }

        public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) => Task.FromResult(Release.AlreadyGone(resource));
    }
}
