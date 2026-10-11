using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class DockerTests
{
    private const string Web = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string Stray = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private const string Containers =
        $$"""
        {"ID":"{{Web}}","Image":"nginx","Names":"web","State":"running","Labels":"snail.owner=session:me,com.docker.compose.project=demo","Networks":"demo_net","Mounts":"demo_data"}
        {"ID":"{{Stray}}","Image":"postgres","Names":"db","State":"running","Labels":"","Networks":"bridge","Mounts":"pgdata"}
        """;

    private const string Networks =
        """
        {"ID":"n1","Name":"bridge","Labels":""}
        {"ID":"n2","Name":"demo_net","Labels":"snail.owner=session:me"}
        """;

    private bool _isDaemonUp = true;

    private readonly FakePrograms _programs = new();

    public DockerTests() => _programs.Answer = Answered;

    private Docker Docker => new(_programs);

    private ProgramRun Answered(string program, IReadOnlyList<string> arguments) => arguments switch
    {
        ["info", ..] => _isDaemonUp ? FakePrograms.Said("29.4.0") : FakePrograms.Failed("Cannot connect to the Docker daemon"),
        ["ps", "-a", "--no-trunc", "--filter", ..] => FakePrograms.Said(Containers.Split('\n')[0]),
        ["ps", ..] => FakePrograms.Said(Containers),
        ["network", "ls", ..] => FakePrograms.Said(Networks),
        ["volume", "ls", ..] => FakePrograms.Said("""{"Name":"demo_data","Labels":"snail.owner=session:me"}"""),
        ["system", "df", ..] => FakePrograms.Said("""{"Type":"Build Cache","Reclaimable":"1.21GB","Size":"2.5GB"}"""),
        ["image", "ls", "--filter", "dangling=true", ..] => FakePrograms.Said("{\"ID\":\"1\",\"Size\":\"300MB\"}\n{\"ID\":\"2\",\"Size\":\"200MB\"}"),
        ["buildx", "history", "ls", ..] => FakePrograms.Said(
            "{\"ref\":\"orbstack/orbstack/old1\",\"created_at\":\"2026-10-09T10:00:00Z\"}\n{\"ref\":\"orbstack/orbstack/today1\",\"created_at\":\"2026-10-11T10:00:00Z\"}"),
        _ => FakePrograms.Said(string.Empty)
    };

    /// <summary>Starting a virtual machine of gigabytes to ask what is in it is the opposite of cleaning up.</summary>
    [Fact]
    public async Task Docker_NotRunning_IsSaid_AndNothingElseIsAsked()
    {
        _isDaemonUp = false;

        var observed = await new ContainerKind(Docker).ObserveAsync([], CancellationToken.None);

        Assert.Contains("not running", observed.Note, StringComparison.Ordinal);
        Assert.Equal(["docker info --format {{.ServerVersion}}"], _programs.Calls);
    }

    [Fact]
    public async Task Container_WithTheOwnerLabel_IsItsOwners_WithoutItIsNobodys()
    {
        var containers = (await new ContainerKind(Docker).ObserveAsync([], CancellationToken.None)).Resources;

        Assert.Equal(Owner.Session("me"), containers.Single(container => container.Key == Web).Owner);
        Assert.Equal(Evidence.Label, containers.Single(container => container.Key == Web).Evidence);
        Assert.False(containers.Single(container => container.Key == Stray).Owner.IsKnown);
    }

    [Fact]
    public async Task NetworkAndVolume_GoAfterTheContainersUsingThem_TheBuiltinNetworksAreNotResources()
    {
        var networks = (await new NetworkKind(Docker).ObserveAsync([], CancellationToken.None)).Resources;
        var volume = Assert.Single((await new VolumeKind(Docker).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal("network demo_net", Assert.Single(networks).Title);
        Assert.Equal([$"container:{Web}"], networks[0].After);
        Assert.Equal([$"container:{Web}"], volume.After);
    }

    [Fact]
    public async Task Plan_TakesAContainerApartBeforeItsNetworkAndItsVolume()
    {
        List<Resource> resources =
        [
            .. (await new VolumeKind(Docker).ObserveAsync([], CancellationToken.None)).Resources,
            .. (await new NetworkKind(Docker).ObserveAsync([], CancellationToken.None)).Resources,
            .. (await new ContainerKind(Docker).ObserveAsync([], CancellationToken.None)).Resources
        ];

        var inventory = Things.Inventory(resources) with { Asker = Owner.Session("me"), Sessions = [Things.Alive(Owner.Session("me"))] };

        Assert.Equal([Kinds.Container, Kinds.Network, Kinds.Volume], Planner.Decide(inventory, Scope.Mine, "plan").Removals.Select(removal => removal.Resource.Kind));
    }

    [Fact]
    public async Task BuildCache_IsASharedCache_WithTheSizeItWouldGiveBack()
    {
        var cache = Assert.Single((await new BuildCacheKind(Docker).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(cache.IsShared);
        Assert.True(cache.IsRegenerable);
        Assert.Equal(1_210_000_000, cache.Bytes);
    }

    [Fact]
    public async Task DanglingImages_AreOneSharedCache_PrunedOnlyByImagePrune()
    {
        var dangling = Assert.Single((await new DanglingImageKind(Docker).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(dangling.IsShared);
        Assert.Equal(500_000_000, dangling.Bytes);
        Assert.Equal(Outcome.Released, (await new DanglingImageKind(Docker).ReleaseAsync(dangling, CancellationToken.None)).Outcome);
        Assert.Contains("docker image prune -f", _programs.Calls);
    }

    /// <summary>Today's records are what a failed build is read from; only the older ones go.</summary>
    [Fact]
    public async Task BuildRecords_OlderThanADay_GoByTheirRefs_TodaysStay()
    {
        var kind = new BuildHistoryKind(Docker, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)));
        var records = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Contains("1 BuildKit record", records.Title, StringComparison.Ordinal);
        Assert.Equal(Outcome.Released, (await kind.ReleaseAsync(records, CancellationToken.None)).Outcome);
        Assert.Contains("docker buildx history rm old1", _programs.Calls);
    }

    [Theory]
    [InlineData("1.21GB", 1_210_000_000L)]
    [InlineData("512.5kB", 512_500L)]
    [InlineData("0B", 0L)]
    [InlineData("3.4MB (12%)", 3_400_000L)]
    public void Size_IsReadTheWayDockerPrintsIt(string written, long expected) =>
        Assert.Equal(expected, Snail.MCP.CleanUp.Adapters.Docker.Bytes(written));

    [Fact]
    public async Task Container_WhoseOwnerLabelChanged_IsLeftAlone()
    {
        var web = (await new ContainerKind(Docker).ObserveAsync([], CancellationToken.None)).Resources.Single(container => container.Key == Web);

        var release = await new ContainerKind(Docker).ReleaseAsync(web with { Owner = Owner.Session("someone else") }, CancellationToken.None);

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.DoesNotContain(_programs.Calls, call => call.StartsWith("docker rm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Container_Released_IsRemovedByItsId()
    {
        var web = (await new ContainerKind(Docker).ObserveAsync([], CancellationToken.None)).Resources.Single(container => container.Key == Web);

        Assert.Equal(Outcome.Released, (await new ContainerKind(Docker).ReleaseAsync(web, CancellationToken.None)).Outcome);
        Assert.Contains($"docker rm -f {Web}", _programs.Calls);
    }
}
