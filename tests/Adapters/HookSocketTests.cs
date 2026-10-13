using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class HookSocketTests : IAsyncDisposable
{
    private readonly Scratch _scratch = new();

    private readonly string _run = Path.Combine(Path.GetTempPath(), $"snail-{Guid.NewGuid():n}"[..14]);

    private readonly LedgerFile _file;

    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly List<HookSocket> _sockets = [];

    public HookSocketTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private ServerConfig Config => new() { DataDirectory = _scratch.Path, HookDirectory = _run };

    private async Task<HookSocket> ListeningAsync()
    {
        var sessions = new FakeSessions(Me, Alive(Me), Alive(Neighbour));
        var ledger = new SqliteLedger(_file);
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
        var census = new Census([_processes], [], sessions, ledger, new JsonRules(_scratch.File("rules.json"), []), new SqliteBaselines(_file), new SqliteDiscoveries(_file), clock);
        var hooks = new Hooks(new MeasuredCensus(census, new Performance(clock)), new Cleaner([_processes], ledger), Config, NullLogger<Hooks>.Instance, clock);
        var socket = new HookSocket(Config, hooks, NullLogger<HookSocket>.Instance, TimeProvider.System);

        _sockets.Add(socket);
        await socket.StartAsync(CancellationToken.None);

        for (var wait = 0; wait < 50 && !File.Exists(Config.HookSocket); wait++)
            await Task.Delay(20);

        return socket;
    }

    private static string Event(string name, string session, string? reason = null) =>
        new JsonObject { ["hook_event_name"] = name, ["session_id"] = session, ["reason"] = reason }.ToJsonString();

    private async Task<string> HandAsync(string input)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        await client.ConnectAsync(new UnixDomainSocketEndPoint(Config.HookSocket));
        await client.SendAsync(Encoding.UTF8.GetBytes(input));
        client.Shutdown(SocketShutdown.Send);

        var answer = new byte[512];
        var read = await client.ReceiveAsync(answer, SocketFlags.None);

        return Encoding.UTF8.GetString(answer, 0, read).Trim();
    }

    /// <summary>A session end handed over is swept by the server, as the hook process would have swept it.</summary>
    [Fact]
    public async Task SessionEnd_HandedOver_IsSweptByTheServer_AndAnsweredOk()
    {
        _processes.Present.Add(Of(Kinds.Process, "1@1", Me, Evidence.Ledger));
        _processes.Present.Add(Of(Kinds.Process, "2@1", Neighbour, Evidence.Ledger));
        await ListeningAsync();

        Assert.Equal("ok", await HandAsync(Event("SessionEnd", Me.Id, "logout")));
        Assert.Equal(["process:1@1"], _processes.Released);
    }

    /// <summary>Whoever can write to the socket can sweep a session, so nobody but its user may reach it.</summary>
    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Socket_IsReachableByItsUserAlone()
    {
        await ListeningAsync();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_run));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Config.HookSocket));
    }

    [Fact]
    public async Task SecondServer_LeavesTheSocketToTheOneAnswering_AndTheFirstRemovesItWhenItStops()
    {
        var first = await ListeningAsync();
        var second = await ListeningAsync();

        Assert.Equal("ok", await HandAsync(Event("Notification", Me.Id)));

        await second.StopAsync(CancellationToken.None);
        Assert.True(File.Exists(Config.HookSocket));

        await first.StopAsync(CancellationToken.None);
        Assert.False(File.Exists(Config.HookSocket));
    }

    /// <summary>A crashed server leaves its socket behind; the next one takes it over instead of giving up.</summary>
    [Fact]
    public async Task Socket_LeftByAServerThatDied_IsTakenOver()
    {
        Directory.CreateDirectory(_run);

        using (var dead = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            dead.Bind(new UnixDomainSocketEndPoint(Config.HookSocket));

        await ListeningAsync();

        Assert.Equal("ok", await HandAsync(Event("Notification", Me.Id)));
    }

    [Fact]
    public async Task Forwarder_HandsTheEventToAServer_AndSaysWhenNoneAnswers()
    {
        Assert.False(await Forwarding.WasHandedAsync(Config.HookSocket, Encoding.UTF8.GetBytes(Event("Notification", Me.Id))));

        await ListeningAsync();

        Assert.True(await Forwarding.WasHandedAsync(Config.HookSocket, Encoding.UTF8.GetBytes(Event("Notification", Me.Id))));
    }

    [Fact]
    public void Hook_ThroughTheForwarder_IsStillThisServersOwn()
    {
        var command = Invocation.Forwarded("/opt/snail/s3l-mcp-cleanup-hook", "/Users/me/.s3l-mcp-cleanup/run/hooks.sock", Hooks.Verb);

        Assert.StartsWith("/opt/snail/s3l-mcp-cleanup-hook /Users/me/.s3l-mcp-cleanup/run/hooks.sock ", command, StringComparison.Ordinal);
        Assert.EndsWith(" hook", command, StringComparison.Ordinal);
        Assert.True(HookInstallation.IsOwn(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["command"] = command }) }));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var socket in _sockets)
        {
            await socket.StopAsync(CancellationToken.None);
            socket.Dispose();
        }

        _file.Dispose();
        _scratch.Dispose();

        if (Directory.Exists(_run))
            Directory.Delete(_run, recursive: true);
    }
}
