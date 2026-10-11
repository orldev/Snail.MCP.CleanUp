using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class SimulatorTests : IDisposable
{
    private const string Mine = "11111111-2222-3333-4444-555555555555";

    private const string Theirs = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";

    private const string Session = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeProcessTable _table = new();

    private readonly FakePrograms _programs = new();

    private string _devices = Listed(("snail-2647fa9c-8ea1-4ee7-a1f0-7053be900dd8-1", Mine, "Booted"), ("iPhone 17 Pro", Theirs, "Booted"));

    public SimulatorTests()
    {
        _file = new LedgerFile(_scratch.File("ledger.db"));
        _programs.Answer = (program, arguments) => arguments is ["simctl", "list", "devices", "-j"] ? FakePrograms.Said(_devices) : FakePrograms.Said(Mine);
    }

    private SimulatorKind Kind => new(_programs, _table, TimeProvider.System);

    private Leases LeasesFor(Owner owner, SqliteLedger ledger)
    {
        var config = new ServerConfig { DataDirectory = _scratch.Path, AndroidSdk = _scratch.File("sdk"), AvdDirectory = _scratch.File("avd") };

        return new Leases(_programs, new FakeSessions(owner), ledger, new Android(config, _programs), _table, config, TimeProvider.System);
    }

    private static string Listed(params (string Name, string Udid, string State)[] devices) =>
        new JsonObject
        {
            ["devices"] = new JsonObject
            {
                ["com.apple.CoreSimulator.SimRuntime.iOS-27-0"] = new JsonArray([.. devices.Select(device => (JsonNode)new JsonObject
                {
                    ["udid"] = device.Udid,
                    ["name"] = device.Name,
                    ["state"] = device.State,
                    ["isAvailable"] = true,
                    ["dataPath"] = $"/sim/{device.Udid}/data"
                })])
            }
        }.ToJsonString();

    [Fact]
    public void Name_GivenToAnOwnersSimulator_SaysWhoseItIs_EvenWithDashesInTheSession()
    {
        var owner = Owner.Session(Session);

        Assert.Equal(owner, Simulators.OwnerOf(Simulators.NameFor(owner, 3)));
        Assert.Equal(3, Simulators.NumberOf(Simulators.NameFor(owner, 3)));
        Assert.Equal(Owner.Nobody, Simulators.OwnerOf("iPhone 17 Pro"));
        Assert.Equal(Owner.Nobody, Simulators.OwnerOf("snail-walk"));
    }

    /// <summary>An Android device refuses to start under a name with @, which a session known by its process has.</summary>
    [Fact]
    public void Name_OfASessionKnownByItsProcess_UsesOnlyCharactersDevicesAllow_AndReadsBack()
    {
        var owner = Owner.Session("pid-17181@1791737784");
        var name = Simulators.NameFor(owner, 1);

        Assert.Matches("^[A-Za-z0-9._-]+$", name);
        Assert.Equal(owner, Simulators.OwnerOf(name));
    }

    /// <summary>`simctl shutdown all` once stopped another agent's test run; only a UDID can address a simulator here.</summary>
    [Theory]
    [InlineData("all")]
    [InlineData("booted")]
    [InlineData("iPhone 17 Pro")]
    public void OnlyAUdid_IsAUdid(string text) => Assert.False(Simulators.IsUdid(text));

    [Fact]
    public void Runtime_IsSpokenTheWayPeopleSayIt() =>
        Assert.Equal("iOS 27.0", Simulators.Spoken("com.apple.CoreSimulator.SimRuntime.iOS-27-0"));

    [Fact]
    public async Task SimulatorNamedForASession_IsItsByLabel_OthersAreNobodys()
    {
        var observed = await Kind.ObserveAsync([], CancellationToken.None);

        Assert.Equal(Owner.Session(Session), observed.Resources.Single(resource => resource.Key == Mine).Owner);
        Assert.Equal(Evidence.Label, observed.Resources.Single(resource => resource.Key == Mine).Evidence);
        Assert.False(observed.Resources.Single(resource => resource.Key == Theirs).Owner.IsKnown);
    }

    [Fact]
    public async Task ProgramDrivingASimulator_MustGoBeforeIt()
    {
        var runner = _table.Add(700, 1, $"xcodebuild test-without-building -destination id={Mine}");

        var simulator = (await Kind.ObserveAsync([], CancellationToken.None)).Resources.Single(resource => resource.Key == Mine);

        Assert.Equal([$"process:{runner.Key}"], simulator.After);
    }

    [Fact]
    public async Task Release_StopsItsDrivers_ThenShutsDownAndDeletes_ByUdid()
    {
        _table.Add(700, 1, $"xcodebuild test-without-building -destination id={Mine}");
        var simulator = (await Kind.ObserveAsync([], CancellationToken.None)).Resources.Single(resource => resource.Key == Mine);

        var release = await Kind.ReleaseAsync(simulator, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Equal(
            ["kill -TERM 700", $"xcrun simctl shutdown {Mine}", $"xcrun simctl delete {Mine}"],
            _programs.Calls.Where(call => !call.Contains("list", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Release_NeverSaysAllOrBooted()
    {
        var simulator = (await Kind.ObserveAsync([], CancellationToken.None)).Resources.Single(resource => resource.Key == Mine);

        await Kind.ReleaseAsync(simulator, CancellationToken.None);
        await Kind.ReleaseAsync(simulator with { Key = "all" }, CancellationToken.None);

        Assert.DoesNotContain(_programs.Calls, call => call.EndsWith(" all", StringComparison.Ordinal) || call.EndsWith(" booted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Release_OfASimulatorRenamedSinceThePlan_TouchesNothing()
    {
        var simulator = (await Kind.ObserveAsync([], CancellationToken.None)).Resources.Single(resource => resource.Key == Mine);

        _devices = Listed(("developer's phone", Mine, "Booted"));

        Assert.Equal(Outcome.Changed, (await Kind.ReleaseAsync(simulator, CancellationToken.None)).Outcome);
        Assert.DoesNotContain(_programs.Calls, call => call.Contains("delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lease_NamesTheNextSimulatorForTheSession_AndWritesItDown()
    {
        var calls = new List<IReadOnlyList<string>>();

        _programs.Answer = (program, arguments) =>
        {
            calls.Add(arguments);

            return arguments switch
            {
                ["simctl", "list", "devices", "-j"] => FakePrograms.Said(_devices),
                ["simctl", "list", "devicetypes", "-j"] => FakePrograms.Said("""{"devicetypes":[{"name":"iPhone 17 Pro","identifier":"com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro","productFamily":"iPhone"}]}"""),
                ["simctl", "list", "runtimes", "-j"] => FakePrograms.Said("""{"runtimes":[{"identifier":"com.apple.CoreSimulator.SimRuntime.iOS-27-0","platform":"iOS","version":"27.0","isAvailable":true}]}"""),
                _ => FakePrograms.Said("99999999-2222-3333-4444-555555555555\n")
            };
        };

        var ledger = new SqliteLedger(_file);
        var leases = LeasesFor(Owner.Session(Session), ledger);
        var answer = await leases.SimulatorAsync(null, boot: false, CancellationToken.None);

        Assert.True(answer.IsSuccess, answer.Error);
        Assert.Equal($"snail-{Session}-2", answer.Data!["name"]!.GetValue<string>());
        Assert.Contains(calls, arguments => arguments is ["simctl", "create", _, "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro", "com.apple.CoreSimulator.SimRuntime.iOS-27-0"]);
        Assert.Equal(Evidence.Ledger, Assert.Single(await ledger.HoldingsAsync(CancellationToken.None)).Evidence);
    }

    [Fact]
    public async Task Lease_ClonesOnlyADeviceThatIsShutDown()
    {
        var leases = LeasesFor(Owner.Session(Session), new SqliteLedger(_file));

        var answer = await leases.SimulatorAsync("iPhone 17 Pro", boot: false, CancellationToken.None);

        Assert.False(answer.IsSuccess);
        Assert.Contains("shut down", answer.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lease_WithoutASession_MakesNothing()
    {
        var answer = await LeasesFor(Owner.Nobody, new SqliteLedger(_file))
            .SimulatorAsync(null, boot: false, CancellationToken.None);

        Assert.False(answer.IsSuccess);
        Assert.Empty(_programs.Calls);
    }

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
