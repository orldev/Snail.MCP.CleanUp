using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class AndroidTests : IDisposable
{
    private const string Session = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private const string Device = $"snail-{Session}-1";

    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _table = new();

    private readonly FakePrograms _programs = new();

    private bool _isAdbRunning;

    public AndroidTests()
    {
        _programs.Answer = (program, arguments) => program == "lsof" && _isAdbRunning ? FakePrograms.Said("p50\ncadb\nn127.0.0.1:5037\n") : FakePrograms.Said(string.Empty);
        Directory.CreateDirectory(_scratch.File("sdk/system-images/android-37.0/google_apis_playstore_ps16k/arm64-v8a"));
        Directory.CreateDirectory(_scratch.File("sdk/system-images/android-37.0/google_apis_ps16k/arm64-v8a"));
        Directory.CreateDirectory(_scratch.File("sdk/system-images/android-34/google_apis/arm64-v8a"));
        File.WriteAllText(_scratch.File("sdk/system-images/android-37.0/google_apis_playstore_ps16k/arm64-v8a/system.img"), string.Empty);
        File.WriteAllText(_scratch.File("sdk/system-images/android-37.0/google_apis_ps16k/arm64-v8a/system.img"), string.Empty);
        File.WriteAllText(_scratch.File("sdk/system-images/android-34/google_apis/arm64-v8a/system.img"), string.Empty);
    }

    private Android Android => new(new ServerConfig { AndroidSdk = _scratch.File("sdk"), AvdDirectory = _scratch.File("home/avd") }, _programs);

    private RunningProcess Emulating(int pid = 900, int port = 5556) =>
        _table.Add(
            pid,
            1,
            $"{_scratch.Path}/sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64-headless -avd {Device} -port {port} -no-snapshot-save",
            executable: $"{_scratch.Path}/sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64-headless");

    [Fact]
    public void Images_PutTheNewestApiFirst_AndThePlayStoreLast() =>
        Assert.Equal(
            ["system-images/android-37.0/google_apis_ps16k/arm64-v8a/", "system-images/android-37.0/google_apis_playstore_ps16k/arm64-v8a/", "system-images/android-34/google_apis/arm64-v8a/"],
            Android.Images().Select(image => image.RelativePath));

    /// <summary>avdmanager needs Java and the command-line tools; the definition it writes is two small files.</summary>
    [Fact]
    public void Define_WritesTheTwoFilesAnEmulatorBootsFrom()
    {
        var directory = Android.Define(Device, Android.Images()[0]);
        var config = File.ReadAllText(Path.Combine(directory, "config.ini"));

        Assert.Contains("image.sysdir.1=system-images/android-37.0/google_apis_ps16k/arm64-v8a/", config, StringComparison.Ordinal);
        Assert.Contains($"path={directory}", File.ReadAllText(_scratch.File($"home/avd/{Device}.ini")), StringComparison.Ordinal);
        Assert.Equal([Device], Android.DeviceNames());
    }

    [Fact]
    public void CommandLine_OfAnEmulator_SaysItsDeviceAndPort()
    {
        var command = $"qemu-system-aarch64 -avd {Device} -port 5556 -no-window";

        Assert.Equal(Device, Snail.MCP.CleanUp.Adapters.Android.DeviceOf(command));
        Assert.Equal(5556, Snail.MCP.CleanUp.Adapters.Android.PortOf(command));
        Assert.Null(Snail.MCP.CleanUp.Adapters.Android.DeviceOf("node server.js"));
    }

    [Fact]
    public async Task Emulator_OfADeviceNamedForASession_IsItsByLabel()
    {
        Emulating();

        var emulator = Assert.Single((await new EmulatorKind(Android, _table, _programs, TimeProvider.System).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(Owner.Session(Session), emulator.Owner);
        Assert.Equal(Evidence.Label, emulator.Evidence);
        Assert.Equal("Android emulator " + Device + " (emulator-5556)", emulator.Title);
    }

    /// <summary>Starting adb's server just to say goodbye would leave a daemon behind; without it, SIGTERM ends the emulator.</summary>
    [Fact]
    public async Task Emulator_WhenAdbIsNotRunning_IsEndedWithoutStartingIt_AndItsPortFileGoes()
    {
        var process = Emulating();
        var kind = new EmulatorKind(Android, _table, _programs, TimeProvider.System);
        var emulator = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        Directory.CreateDirectory(_scratch.File("home"));
        File.WriteAllText(_scratch.File("home/modem-nv-ram-5556"), "nv");
        File.SetLastWriteTimeUtc(_scratch.File("home/modem-nv-ram-5556"), process.StartedAt.UtcDateTime.AddMinutes(1));
        _programs.Answer = (program, arguments) =>
        {
            if (program == "kill" && arguments[0] == "-TERM")
                _table.Running.Remove(process);

            return FakePrograms.Said(string.Empty);
        };

        var release = await kind.ReleaseAsync(emulator, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Equal("shut down on SIGTERM", release.Detail);
        Assert.DoesNotContain(_programs.Calls, call => call.Contains("adb", StringComparison.Ordinal));
        Assert.False(File.Exists(_scratch.File("home/modem-nv-ram-5556")));
    }

    [Fact]
    public async Task PortFile_WrittenBeforeTheEmulatorStarted_IsSomeEarlierRunsAndStays()
    {
        var process = Emulating();
        var kind = new EmulatorKind(Android, _table, _programs, TimeProvider.System);
        var emulator = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        Directory.CreateDirectory(_scratch.File("home"));
        File.WriteAllText(_scratch.File("home/modem-nv-ram-5556"), "nv");
        File.SetLastWriteTimeUtc(_scratch.File("home/modem-nv-ram-5556"), process.StartedAt.UtcDateTime.AddDays(-1));
        _programs.Answer = (program, arguments) =>
        {
            if (program == "kill")
                _table.Running.Remove(process);

            return FakePrograms.Said(string.Empty);
        };

        await kind.ReleaseAsync(emulator, CancellationToken.None);

        Assert.True(File.Exists(_scratch.File("home/modem-nv-ram-5556")));
    }

    [Fact]
    public async Task Emulator_WhenAdbIsRunning_IsAskedToShutDown()
    {
        var process = Emulating();
        var kind = new EmulatorKind(Android, _table, _programs, TimeProvider.System);
        var emulator = Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources);

        _isAdbRunning = true;
        var answer = _programs.Answer;
        _programs.Answer = (program, arguments) =>
        {
            if (arguments.Contains("emu"))
                _table.Running.Remove(process);

            return answer(program, arguments);
        };

        var release = await kind.ReleaseAsync(emulator, CancellationToken.None);

        Assert.Equal("shut down through adb emu kill", release.Detail);
        Assert.Contains(_programs.Calls, call => call.EndsWith("-s emulator-5556 emu kill", StringComparison.Ordinal));
    }

    /// <summary>The starter leaves as soon as the machine runs; an emulator keyed by it was «gone» while the device booted.</summary>
    [Fact]
    public async Task Emulator_IsTheMachineRunningTheDevice_NotTheProgramThatStartedIt()
    {
        _table.Add(800, 1, $"{_scratch.Path}/sdk/emulator/emulator -avd {Device} -port 5556");

        Assert.Empty((await new EmulatorKind(Android, _table, _programs, TimeProvider.System).ObserveAsync([], CancellationToken.None)).Resources);

        Emulating();

        Assert.Equal(Device, Assert.Single((await new EmulatorKind(Android, _table, _programs, TimeProvider.System).ObserveAsync([], CancellationToken.None)).Resources).Key);
    }

    [Fact]
    public async Task VirtualDevice_MadeForASession_IsErasable_AndWaitsForItsEmulator()
    {
        Android.Define(Device, Android.Images()[0]);
        var emulator = Emulating();

        var device = Assert.Single((await new VirtualDeviceKind(Android, _table, new Sizes(TimeProvider.System)).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(Owner.Session(Session), device.Owner);
        Assert.True(device.IsRegenerable);
        Assert.Equal([$"emulator:{Device}"], device.After);
        Assert.Equal(Outcome.Failed, (await new VirtualDeviceKind(Android, _table, new Sizes(TimeProvider.System)).ReleaseAsync(device, CancellationToken.None)).Outcome);

        _table.Running.Clear();

        Assert.Equal(Outcome.Released, (await new VirtualDeviceKind(Android, _table, new Sizes(TimeProvider.System)).ReleaseAsync(device, CancellationToken.None)).Outcome);
        Assert.Empty(Android.DeviceNames());
        Assert.False(Directory.Exists(_scratch.File($"home/avd/{Device}.avd")));
    }

    [Fact]
    public async Task Forwards_AreNotLookedFor_UnlessAdbIsAlreadyRunning()
    {
        Directory.CreateDirectory(_scratch.File("sdk/platform-tools"));
        File.WriteAllText(_scratch.File("sdk/platform-tools/adb"), string.Empty);

        Assert.Empty((await new ForwardKind(Android, _programs).ObserveAsync([], CancellationToken.None)).Resources);
        Assert.DoesNotContain(_programs.Calls, call => call.Contains("forward", StringComparison.Ordinal));
    }

    [Fact]
    public void Forwards_AreReadWithTheEndRemoveTakes()
    {
        var forward = Assert.Single(ForwardKind.Parse("emulator-5554 tcp:5180 tcp:5180\n", "reverse"));

        Assert.Equal("tcp:5180", forward.Facts["local"]);
        Assert.False(forward.Owner.IsKnown);
    }

    public void Dispose() => _scratch.Dispose();
}
