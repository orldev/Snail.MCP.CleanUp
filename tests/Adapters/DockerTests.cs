using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class DockerTests : IDisposable
{
    private const string Web = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string Stray = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private const string WebContainer =
        $$$$"""
        {"Id":"{{{{Web}}}}","Names":["/web"],"Image":"nginx","State":"running","Labels":{"snail.owner":"session:me","com.docker.compose.project":"demo"},
         "NetworkSettings":{"Networks":{"demo_net":{}}},"Mounts":[{"Type":"volume","Name":"demo_data"},{"Type":"bind","Source":"/tmp"}]}
        """;

    private const string Containers =
        $$$$"""
        [{{{{WebContainer}}}},
         {"Id":"{{{{Stray}}}}","Names":["/db"],"Image":"postgres","State":"running","Labels":{},"NetworkSettings":{"Networks":{"bridge":{}}},"Mounts":[{"Type":"volume","Name":"pgdata"}]}]
        """;

    private const string Networks =
        """
        [{"Id":"n1","Name":"bridge","Labels":{}},{"Id":"n2","Name":"demo_net","Labels":{"snail.owner":"session:me"}}]
        """;

    private readonly FakeEngine _engine = new();

    private readonly FakePrograms _programs = new();

    private readonly DockerSocket _socket;

    public DockerTests()
    {
        _engine.Answer = Answered;
        _programs.Answer = (_, arguments) => arguments switch
        {
            ["buildx", "history", "ls", ..] => FakePrograms.Said(
                "{\"ref\":\"orbstack/orbstack/old1\",\"created_at\":\"2026-10-09T10:00:00Z\"}\n{\"ref\":\"orbstack/orbstack/today1\",\"created_at\":\"2026-10-11T10:00:00Z\"}"),
            _ => FakePrograms.Said(string.Empty)
        };
        _socket = new DockerSocket(_engine.Path, TimeProvider.System);
    }

    public void Dispose()
    {
        _socket.Dispose();
        _engine.Dispose();
    }

    private static (int, string) Answered(string method, string path) => (method, path) switch
    {
        ("GET", "/_ping") => (200, "OK"),
        ("GET", "/containers/json?all=1") => (200, Containers),
        ("GET", var one) when one.StartsWith("/containers/json?all=1&filters=", StringComparison.Ordinal) =>
            (200, one.Contains(Web, StringComparison.Ordinal) ? $"[{WebContainer}]" : "[]"),
        ("GET", "/networks") => (200, Networks),
        ("GET", "/volumes") => (200, """{"Volumes":[{"Name":"demo_data","Labels":{"snail.owner":"session:me"}}],"Warnings":null}"""),
        ("GET", var images) when images.Contains("dangling", StringComparison.Ordinal) => (200, """[{"Id":"sha256:1","RepoTags":[],"Size":300000000},{"Id":"sha256:2","RepoTags":null,"Size":200000000}]"""),
        ("GET", var images) when images.StartsWith("/images/json", StringComparison.Ordinal) =>
            (200, """[{"Id":"sha256:abcdef0123456789abcdef","RepoTags":["demo-web:latest"],"Size":42,"Labels":{"snail.owner":"session:me"}}]"""),
        ("GET", "/system/df?type=build-cache") => (200, """{"BuildCache":[{"Size":1000000000,"InUse":false,"Shared":false},{"Size":210000000,"InUse":false,"Shared":false},{"Size":500,"InUse":true,"Shared":false},{"Size":700,"InUse":false,"Shared":true}]}"""),
        ("DELETE", $"/containers/{Web}?force=1") => (204, string.Empty),
        ("DELETE", var gone) when gone.StartsWith("/networks/", StringComparison.Ordinal) => (404, """{"message":"network not found"}"""),
        ("DELETE", var used) when used.StartsWith("/volumes/", StringComparison.Ordinal) => (409, """{"message":"volume is in use"}"""),
        ("POST", var prune) when prune.StartsWith("/images/prune", StringComparison.Ordinal) || prune.StartsWith("/build/prune", StringComparison.Ordinal) => (200, "{}"),
        _ => (404, """{"message":"page not found"}""")
    };

    /// <summary>Starting a virtual machine of gigabytes to ask what is in it is the opposite of cleaning up.</summary>
    [Fact]
    public async Task Docker_WithoutASocket_IsSaidNotRunning_AndNothingIsStarted()
    {
        using var none = new DockerSocket(null, TimeProvider.System);

        var observed = await new ContainerKind(none).ObserveAsync([], CancellationToken.None);

        Assert.Contains("not running", observed.Note, StringComparison.Ordinal);
        Assert.Empty(_programs.Calls);
    }

    [Fact]
    public async Task Docker_WhoseSocketNobodyAnswers_IsSaidNotRunning()
    {
        using var dead = new DockerSocket(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snail-{Guid.NewGuid():n}.sock"), TimeProvider.System);

        Assert.Contains("not running", await dead.UnreachableAsync(CancellationToken.None), StringComparison.Ordinal);
    }

    /// <summary>
    /// A stopped engine's socket was found first and hid the one that ran, and an engine started after the server was
    /// never seen: the places are looked at again, and the first that answers is used.
    /// </summary>
    [Fact]
    public async Task Docker_BehindTheSocketOfAStoppedEngine_IsFoundWhereItAnswers()
    {
        var stopped = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snail-{Guid.NewGuid():n}.sock");
        List<string> places = [stopped];
        using var socket = new DockerSocket(TimeProvider.System, () => places);

        Assert.NotNull(await socket.UnreachableAsync(CancellationToken.None));

        places.Add(_engine.Path);

        var containers = (await new ContainerKind(socket).ObserveAsync([], CancellationToken.None)).Resources;

        Assert.Contains(containers, container => container.Key == Web);
    }

    [Fact]
    public async Task Container_WithTheOwnerLabel_IsItsOwners_WithoutItIsNobodys()
    {
        var containers = (await new ContainerKind(_socket).ObserveAsync([], CancellationToken.None)).Resources;

        Assert.Equal(Owner.Session("me"), containers.Single(container => container.Key == Web).Owner);
        Assert.Equal(Evidence.Label, containers.Single(container => container.Key == Web).Evidence);
        Assert.Equal("web", containers.Single(container => container.Key == Web).Facts["name"]);
        Assert.False(containers.Single(container => container.Key == Stray).Owner.IsKnown);
    }

    /// <summary>Every kind of a census asks once: the Engine is read as one snapshot, not once per kind.</summary>
    [Fact]
    public async Task Census_OfEveryDockerKind_ReadsTheEngineOnce()
    {
        await new ContainerKind(_socket).ObserveAsync([], CancellationToken.None);
        await new NetworkKind(_socket).ObserveAsync([], CancellationToken.None);
        await new VolumeKind(_socket).ObserveAsync([], CancellationToken.None);
        await new ImageKind(_socket).ObserveAsync([], CancellationToken.None);
        await new DanglingImageKind(_socket).ObserveAsync([], CancellationToken.None);
        await new BuildCacheKind(_socket).ObserveAsync([], CancellationToken.None);

        Assert.Single(_engine.Requests, request => request == "GET /containers/json?all=1");
        Assert.Single(_engine.Requests, request => request == "GET /system/df?type=build-cache");
    }

    [Fact]
    public async Task NetworkAndVolume_GoAfterTheContainersUsingThem_TheBuiltinNetworksAreNotResources()
    {
        var networks = (await new NetworkKind(_socket).ObserveAsync([], CancellationToken.None)).Resources;
        var volume = Assert.Single((await new VolumeKind(_socket).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal("network demo_net", Assert.Single(networks).Title);
        Assert.Equal([$"container:{Web}"], networks[0].After);
        Assert.Equal([$"container:{Web}"], volume.After);
    }

    [Fact]
    public async Task Plan_TakesAContainerApartBeforeItsNetworkAndItsVolume()
    {
        List<Resource> resources =
        [
            .. (await new VolumeKind(_socket).ObserveAsync([], CancellationToken.None)).Resources,
            .. (await new NetworkKind(_socket).ObserveAsync([], CancellationToken.None)).Resources,
            .. (await new ContainerKind(_socket).ObserveAsync([], CancellationToken.None)).Resources
        ];

        var inventory = Things.Inventory(resources) with { Asker = Owner.Session("me"), Holders = [Things.Alive(Owner.Session("me"))] };

        Assert.Equal([Kinds.Container, Kinds.Network, Kinds.Volume], Planner.Decide(inventory, Scope.Mine, "plan", MachineKinds.Order).Removals.Select(removal => removal.Resource.Kind));
    }

    /// <summary>Only what no build uses and no other record shares is what a prune gives back.</summary>
    [Fact]
    public async Task BuildCache_IsASharedCache_WithTheSizeItWouldGiveBack()
    {
        var cache = Assert.Single((await new BuildCacheKind(_socket).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(cache.IsShared);
        Assert.True(cache.IsRegenerable);
        Assert.Equal(1_210_000_000, cache.Bytes);
    }

    [Fact]
    public async Task DanglingImages_AreOneSharedCache_PrunedOnlyByTheDanglingFilter()
    {
        var dangling = Assert.Single((await new DanglingImageKind(_socket).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(dangling.IsShared);
        Assert.Equal(500_000_000, dangling.Bytes);
        Assert.Equal(Outcome.Released, (await new DanglingImageKind(_socket).ReleaseAsync(dangling, CancellationToken.None)).Outcome);
        Assert.Contains("""POST /images/prune?filters={"dangling":["true"]}""", _engine.Requests);
    }

    [Fact]
    public async Task Image_WithTheOwnerLabel_IsNamedByItsTag()
    {
        var image = Assert.Single((await new ImageKind(_socket).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal("image demo-web:latest", image.Title);
        Assert.Equal(Owner.Session("me"), image.Owner);
        Assert.Equal(42, image.Bytes);
    }

    /// <summary>Today's records are what a failed build is read from; only the older ones go.</summary>
    [Fact]
    public async Task BuildRecords_OlderThanADay_GoByTheirRefs_TodaysStay()
    {
        var kind = new BuildHistoryKind(_socket, new Docker(_programs), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)));
        var records = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Contains("1 BuildKit record", records.Title, StringComparison.Ordinal);
        Assert.Equal(Outcome.Released, (await kind.ReleaseAsync(records, CancellationToken.None)).Outcome);
        Assert.Contains("docker buildx history rm old1", _programs.Calls);
    }

    [Fact]
    public async Task Container_WhoseOwnerLabelChanged_IsLeftAlone()
    {
        var web = (await new ContainerKind(_socket).ObserveAsync([], CancellationToken.None)).Resources.Single(container => container.Key == Web);

        var release = await new ContainerKind(_socket).ReleaseAsync(web with { Owner = Owner.Session("someone else") }, CancellationToken.None);

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.DoesNotContain(_engine.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Container_Released_IsRemovedByItsId_Forced()
    {
        var web = (await new ContainerKind(_socket).ObserveAsync([], CancellationToken.None)).Resources.Single(container => container.Key == Web);

        Assert.Equal(Outcome.Released, (await new ContainerKind(_socket).ReleaseAsync(web, CancellationToken.None)).Outcome);
        Assert.Contains($"DELETE /containers/{Web}?force=1", _engine.Requests);
    }

    [Fact]
    public async Task Container_GoneSinceThePlan_IsAlreadyGone()
    {
        var gone = Things.Of(Kinds.Container, Stray + "0");

        Assert.Equal(Outcome.Gone, (await new ContainerKind(_socket).ReleaseAsync(gone, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Network_TheEngineNoLongerHas_IsAlreadyGone_AVolumeInUse_IsRefusedInTheEnginesWords()
    {
        var networks = (await new NetworkKind(_socket).ObserveAsync([], CancellationToken.None)).Resources;
        var volume = Assert.Single((await new VolumeKind(_socket).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(Outcome.Gone, (await new NetworkKind(_socket).ReleaseAsync(networks[0], CancellationToken.None)).Outcome);

        var refused = await new VolumeKind(_socket).ReleaseAsync(volume, CancellationToken.None);

        Assert.Equal(Outcome.Failed, refused.Outcome);
        Assert.Contains("in use", refused.Detail, StringComparison.Ordinal);
    }
}
