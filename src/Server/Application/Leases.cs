using System.Globalization;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Hands an agent a device of its own instead of one somebody else is using.</summary>
/// <remarks>
/// Two agents on one simulator is how a test run gets stopped in the middle by somebody else's cleanup.
/// A lease ends that at the source: the agent gets a device made for it, named for its session and written
/// down at birth, so that nobody else uses it and only its own cleanup ever removes it.
/// <para>
/// A device is named after the highest number its session already has, and an emulator is given the first console port
/// nothing listens on — both read before the device is made. Two leases asked for at once, two tool calls of one turn,
/// read the same and got the same name (2026-10-16); an emulator listens on its port only seconds after it starts. So
/// devices are made one at a time, and a port given out stays given for as long as an emulator may take to take it.
/// </para>
/// </remarks>
public sealed class Leases(IPrograms programs, IHolders sessions, ILedger ledger, Android android, IProcessTable table, ServerConfig config, TimeProvider clock) : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    private readonly SemaphoreSlim _making = new(1, 1);

    private readonly Dictionary<int, DateTimeOffset> _given = [];

    /// <summary>A simulator of the agent's own: a clone of a named device, or a new one of a device type.</summary>
    public async Task<Answer> SimulatorAsync(string? source, bool boot, CancellationToken cancellationToken)
    {
        var owner = await sessions.CurrentAsync(cancellationToken);

        if (!owner.IsKnown)
            return Answer.Failed(Messages.NoSession, Messages.NoSessionHint);

        var made = await OneAtATimeAsync(() => SimulatorMadeAsync(owner, source, cancellationToken), cancellationToken);

        if (!boot || made.Data is not JsonObject shown)
            return made;

        var isBooted = (await SimctlAsync(["bootstatus", shown["udid"]!.GetValue<string>(), "-b"], cancellationToken)).IsSuccess;

        shown["isBooted"] = isBooted;
        shown["note"] = isBooted ? null : "made, but it did not boot; boot it with xcrun simctl boot and read the error";

        return made;
    }

    /// <summary>An Android emulator of the agent's own: a new virtual device from an installed system image, started on a free port.</summary>
    /// <remarks>
    /// It runs without saving a snapshot on exit, so that the next boot is not a copy of whatever state the
    /// agent left the device in — a test identity, a PIN, an app half set up.
    /// </remarks>
    public async Task<Answer> EmulatorAsync(string? image, bool window, CancellationToken cancellationToken)
    {
        var owner = await sessions.CurrentAsync(cancellationToken);

        if (!owner.IsKnown)
            return Answer.Failed(Messages.NoSession, Messages.NoSessionHint);

        if (!File.Exists(android.Emulator))
            return Answer.Failed($"there is no Android emulator at {android.Emulator}", Messages.EmulatorHint);

        var chosen = android.Images().FirstOrDefault(candidate => string.IsNullOrWhiteSpace(image) || candidate.RelativePath.Contains(image, StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
            return Answer.Failed(string.IsNullOrWhiteSpace(image) ? "no Android system image is installed" : $"no installed system image matches {image}", Messages.EmulatorHint);

        return await OneAtATimeAsync(() => EmulatorStartedAsync(owner, chosen, window, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose() => _making.Dispose();

    /// <summary>Makes one device at a time, so that the next is named and given a port after the last one exists.</summary>
    private async Task<Answer> OneAtATimeAsync(Func<Task<Answer>> made, CancellationToken cancellationToken)
    {
        await _making.WaitAsync(cancellationToken);

        try
        {
            return await made();
        }
        finally
        {
            _making.Release();
        }
    }

    private async Task<Answer> SimulatorMadeAsync(Owner owner, string? source, CancellationToken cancellationToken)
    {
        var devices = Simulators.Parse((await SimctlAsync(["list", "devices", "-j"], cancellationToken)).Output);
        var name = Simulators.NameFor(owner, devices.Where(device => Simulators.OwnerOf(device.Name) == owner).Select(device => Simulators.NumberOf(device.Name)).DefaultIfEmpty().Max() + 1);
        var original = string.IsNullOrWhiteSpace(source) ? null : devices.FirstOrDefault(device => device.Udid == source || device.Name == source);

        if (original is { IsBooted: true })
            return Answer.Failed($"{original.Name} is running, and simctl clones only a device that is shut down", Messages.SimulatorHint);

        var made = original is null ? await CreatedAsync(name, source, cancellationToken) : await SimctlAsync(["clone", original.Udid, name], cancellationToken);

        if (!made.IsSuccess)
            return Answer.Failed($"simctl could not make the simulator: {made.Failure}", Messages.SimulatorHint);

        var udid = made.Output.Trim();

        await ledger.RecordAsync(new Holding($"{MachineKinds.Simulator}:{udid}", owner, Evidence.Ledger, clock.GetUtcNow(), name), cancellationToken);

        return Answer.Ok(new JsonObject
        {
            ["udid"] = udid,
            ["name"] = name,
            ["id"] = $"{MachineKinds.Simulator}:{udid}",
            ["from"] = original?.Name ?? source,
            ["isBooted"] = false,
            ["note"] = null
        });
    }

    private async Task<Answer> EmulatorStartedAsync(Owner owner, Android.Image chosen, bool window, CancellationToken cancellationToken)
    {
        var taken = (await Listeners.ReadAsync(programs, cancellationToken)).Values.SelectMany(ports => ports).ToHashSet();
        var now = clock.GetUtcNow();
        var port = Enumerable.Range(0, 64).Select(step => 5554 + (step * 2)).FirstOrDefault(candidate => IsFree(candidate, taken, now));

        if (port == 0)
            return Answer.Failed("every emulator console port from 5554 to 5680 is taken", Messages.EmulatorHint);

        var name = Simulators.NameFor(owner, android.DeviceNames().Where(device => Simulators.OwnerOf(device) == owner).Select(Simulators.NumberOf).DefaultIfEmpty().Max() + 1);

        android.Define(name, chosen);
        await ledger.RecordAsync(new Holding($"{MachineKinds.VirtualDevice}:{name}", owner, Evidence.Ledger, now, name), cancellationToken);

        var log = Path.Combine(config.DataDirectory, "runs", $"{name}-{clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");

        Directory.CreateDirectory(Path.GetDirectoryName(log)!);

        var started = programs.StartLogged([android.Emulator, "-avd", name, "-port", $"{port}", "-no-snapshot-save", "-no-boot-anim", "-no-audio", .. window ? Array.Empty<string>() : ["-no-window"]], null, log);

        _given[port] = now;
        table.Refresh();

        return Answer.Ok(new JsonObject
        {
            ["avd"] = name,
            ["serial"] = Android.Serial(port),
            ["port"] = port,
            ["image"] = chosen.RelativePath,
            ["pid"] = started.Pid,
            ["log"] = log,
            ["note"] = "booting takes a minute or two; adb -s <serial> wait-for-device waits for it"
        });
    }

    /// <summary>Whether a console port and the adb port after it are free: nothing listens on them, and none was given out lately.</summary>
    private bool IsFree(int port, HashSet<int> taken, DateTimeOffset now)
    {
        if (taken.Contains(port) || taken.Contains(port + 1))
            return false;

        return !_given.TryGetValue(port, out var given) || now - given >= Patience;
    }

    private async Task<ProgramRun> CreatedAsync(string name, string? source, CancellationToken cancellationToken)
    {
        var types = Simulators.DeviceTypes((await SimctlAsync(["list", "devicetypes", "-j"], cancellationToken)).Output);
        var runtimes = Simulators.Runtimes((await SimctlAsync(["list", "runtimes", "-j"], cancellationToken)).Output);
        var type = string.IsNullOrWhiteSpace(source)
            ? types.LastOrDefault(candidate => candidate.Family == "iPhone" && candidate.Name.EndsWith(" Pro", StringComparison.Ordinal)) ?? types.LastOrDefault(candidate => candidate.Family == "iPhone")
            : types.FirstOrDefault(candidate => string.Equals(candidate.Name, source, StringComparison.OrdinalIgnoreCase) || candidate.Identifier == source);
        var runtime = runtimes.LastOrDefault(candidate => candidate.IsAvailable && candidate.Platform == "iOS");

        if (type is null || runtime is null)
            return new ProgramRun(1, string.Empty, type is null ? $"there is no device or device type called {source}" : "no iOS runtime is installed");

        return await SimctlAsync(["create", name, type.Identifier, runtime.Identifier], cancellationToken);
    }

    private Task<ProgramRun> SimctlAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        programs.RunAsync("xcrun", ["simctl", .. arguments], Patience, cancellationToken);
}
