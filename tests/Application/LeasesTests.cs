using System.Collections.Concurrent;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Application;

/// <summary>
/// A lease names the device after the highest number its session already has, and gives an emulator the first console
/// port nothing listens on — both read before the device is made. Two leases asked for at once, two tool calls of one
/// turn, read the same and got the same name (2026-10-16).
/// </summary>
public sealed class LeasesTests : IDisposable
{
    private const string Session = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly Simctl _simctl = new();

    public LeasesTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    private ServerConfig Config => new() { DataDirectory = _scratch.File("data"), AndroidSdk = _scratch.File("sdk"), AvdDirectory = _scratch.File("avd") };

    private Leases LeasesOver(ServerConfig config) =>
        new(_simctl, new FakeSessions(Owner.Session(Session)), new SqliteLedger(_file), new Android(config, _simctl), new FakeProcessTable(), config, TimeProvider.System);

    [Fact]
    public async Task Simulator_TwoLeasesOfOneSessionAtOnce_GetTwoNames()
    {
        using var leases = LeasesOver(Config);

        var answers = await Task.WhenAll(
            leases.SimulatorAsync("iPhone 17 Pro", boot: false, CancellationToken.None),
            leases.SimulatorAsync("iPhone 17 Pro", boot: false, CancellationToken.None));

        Assert.All(answers, answer => Assert.True(answer.IsSuccess, answer.Error));
        Assert.Equal([$"snail-{Session}-1", $"snail-{Session}-2"], _simctl.Created.Order(StringComparer.Ordinal));
    }

    /// <summary>An emulator listens on its port seconds after it starts: the second lease must not be given the first one's port.</summary>
    [Fact]
    public async Task Emulator_TwoLeasesOfOneSessionAtOnce_GetTwoNamesAndTwoPorts()
    {
        var config = Config;
        var emulator = Path.Combine(config.AndroidSdk, "emulator", "emulator");
        var image = Path.Combine(config.AndroidSdk, "system-images", "android-35", "google_apis", "arm64-v8a");

        Directory.CreateDirectory(Path.GetDirectoryName(emulator)!);
        File.WriteAllText(emulator, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(emulator, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateDirectory(image);
        File.WriteAllBytes(Path.Combine(image, "system.img"), []);

        using var leases = LeasesOver(config);

        var answers = await Task.WhenAll(
            leases.EmulatorAsync(null, window: false, CancellationToken.None),
            leases.EmulatorAsync(null, window: false, CancellationToken.None));

        Assert.All(answers, answer => Assert.True(answer.IsSuccess, answer.Error));
        Assert.Equal(2, answers.Select(answer => answer.Data!["avd"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, answers.Select(answer => answer.Data!["port"]!.GetValue<int>()).Distinct().Count());
    }

    /// <summary>simctl as it answers: the devices it made are listed after, and each call takes a moment, so that two callers overlap.</summary>
    private sealed class Simctl : IPrograms
    {
        private int _pid = 4000;

        public ConcurrentQueue<string> Created { get; } = new();

        public async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

            return arguments switch
            {
                ["simctl", "list", "devices", "-j"] => Said(Devices()),
                ["simctl", "list", "devicetypes", "-j"] => Said("""{"devicetypes":[{"name":"iPhone 17 Pro","identifier":"com.apple.iPhone-17-Pro","productFamily":"iPhone"}]}"""),
                ["simctl", "list", "runtimes", "-j"] => Said("""{"runtimes":[{"identifier":"com.apple.iOS-27-0","platform":"iOS","version":"27.0","isAvailable":true}]}"""),
                ["simctl", "create", var name, ..] => Made(name),
                _ => Said(string.Empty)
            };
        }

        public Task<ProgramRun> ReadAsync(string program, IReadOnlyList<string> arguments, TimeSpan freshness, TimeSpan timeout, CancellationToken cancellationToken) =>
            RunAsync(program, arguments, timeout, cancellationToken);

        public StartedProgram StartLogged(IReadOnlyList<string> command, string? directory, string log) =>
            new(Interlocked.Increment(ref _pid), DateTimeOffset.UnixEpoch.AddDays(1));

        public Task<bool> TryDetachAsync(IReadOnlyList<string> command, CancellationToken cancellationToken) => Task.FromResult(true);

        private ProgramRun Made(string name)
        {
            Created.Enqueue(name);

            return Said(Guid.NewGuid().ToString().ToUpperInvariant());
        }

        private string Devices()
        {
            var devices = Created.Select(name => (JsonNode)new JsonObject { ["udid"] = Guid.NewGuid().ToString(), ["name"] = name, ["state"] = "Shutdown" });
            var listed = new JsonObject { ["devices"] = new JsonObject { ["com.apple.CoreSimulator.SimRuntime.iOS-27-0"] = new JsonArray([.. devices]) } };

            return listed.ToJsonString();
        }

        private static ProgramRun Said(string output) => new(0, output, string.Empty);
    }
}
