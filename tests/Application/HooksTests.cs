using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Application;

public sealed class HooksTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeKind _processes = new(Kinds.Process);

    public HooksTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private Hooks Hooks
    {
        get
        {
            var sessions = new FakeSessions(Me, Alive(Me), Alive(Neighbour));
            var ledger = new SqliteLedger(_file);
            var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
            var census = new Census([_processes], [], sessions, ledger, new JsonRules(_scratch.File("rules.json"), []), new SqliteBaselines(_file), new SqliteDiscoveries(_file), clock);

            return new Hooks(new MeasuredCensus(census, new Performance(clock)), new Cleaner([_processes], ledger), new ServerConfig { DataDirectory = _scratch.Path }, NullLogger<Hooks>.Instance, clock);
        }
    }

    private static string Event(string name, string session, string? reason = null) =>
        new JsonObject { ["hook_event_name"] = name, ["session_id"] = session, ["reason"] = reason }.ToJsonString();

    [Fact]
    public void Input_OfAHook_IsReadForItsEventSessionAndReason()
    {
        var (name, session, reason) = Hooks.Read(Event("SessionEnd", "abc", "logout"));

        Assert.Equal("SessionEnd", name);
        Assert.Equal(Owner.Session("abc"), session);
        Assert.Equal("logout", reason);
    }

    [Fact]
    public void Input_ThatIsNotJson_IsNoEvent() =>
        Assert.Equal(string.Empty, Hooks.Read("not json").Name);

    [Fact]
    public async Task SessionEnd_ReleasesWhatTheEndingSessionIsProvenToHaveLeft_AndOnlyThat()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ledger));
        _processes.Present.Add(Of(Kinds.Process, "2@1", Neighbour, Evidence.Ledger));
        _processes.Present.Add(Of(Kinds.Process, "3@1", Me, Evidence.Appeared));

        await Hooks.HandleAsync(Event("SessionEnd", Me.Id, "logout"), CancellationToken.None);

        Assert.Equal(["process:1@1"], _processes.Released);
        Assert.Contains("released process:1@1", await File.ReadAllTextAsync(_scratch.File("sweeps.log")), StringComparison.Ordinal);
    }

    /// <summary>After /clear the work goes on in the same process under a new id, with its dev server still in use.</summary>
    [Fact]
    public async Task SessionEnd_ByClear_SweepsNothing()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ledger));

        await Hooks.HandleAsync(Event("SessionEnd", Me.Id, "clear"), CancellationToken.None);

        Assert.Empty(_processes.Released);
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }
}
