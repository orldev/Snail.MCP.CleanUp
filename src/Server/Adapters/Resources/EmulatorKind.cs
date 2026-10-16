namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Running Android emulators, owned by the name of the virtual device they run or by the shell that started them.</summary>
/// <remarks>
/// An emulator is ended the way it expects: <c>adb emu kill</c> when adb's server is already running, so it
/// shuts down cleanly — and SIGTERM otherwise, because starting adb's server just to say goodbye would leave
/// a daemon behind. A <c>kill -9</c> is the last resort only: a virtual device killed hard can be left
/// unbootable. Once it is gone, the file it wrote for its console port goes with it.
/// <para>
/// An emulator is known by the device it runs, not by a process: the <c>emulator</c> program only starts
/// <c>qemu-system-*</c> and leaves, and a resource keyed by the starter's process would be «gone» a second
/// after it was found while the device went on booting. Measured on 2026-10-11, when exactly that let the
/// device's files be erased under the booting emulator.
/// </para>
/// </remarks>
public sealed class EmulatorKind(Android android, IProcessTable table, IPrograms programs, TimeProvider clock) : IResourceKind
{
    /// <summary>How long an emulator is given to shut down before it is killed.</summary>
    public static TimeSpan Grace { get; } = TimeSpan.FromSeconds(20);

    /// <inheritdoc/>
    public string Name => MachineKinds.Emulator;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);
        var lineage = Lineage.Of(running, holders);
        var machines = running.Where(IsMachine).GroupBy(process => Android.DeviceOf(process.Command)!, StringComparer.Ordinal);

        return new Observation(Name, [.. machines.Select(machine => Described(machine.Key, [.. machine], lineage))], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var device = resource.Key;
        var running = await RunningAsync(device, cancellationToken);

        if (running.Count == 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), clock, cancellationToken);
            running = await RunningAsync(device, cancellationToken);
        }

        if (running.Count == 0)
            return Release.AlreadyGone(resource);

        var port = running.Select(process => Android.PortOf(process.Command)).FirstOrDefault(found => found is not null);
        var how = await EndedAsync(running, port, cancellationToken);

        if (how is null)
            return Release.Refused(resource, $"{device} would not end");

        Forget(port, running.Min(process => process.StartedAt));

        return Release.Done(resource, await TryStopAdbAsync(running, cancellationToken) ? $"{how}; the adb server it started was stopped too" : how);
    }

    /// <summary>Whether a program is the machine an emulator runs a device in, rather than the program that started it.</summary>
    public static bool IsMachine(RunningProcess process) =>
        process.Program.StartsWith("qemu-system", StringComparison.Ordinal) && Android.DeviceOf(process.Command) is not null;

    /// <summary>Every program running the device, its starter included, read fresh.</summary>
    private async Task<IReadOnlyList<RunningProcess>> RunningAsync(string device, CancellationToken cancellationToken)
    {
        table.Refresh();

        return [.. (await table.ListAsync(cancellationToken)).Where(process => Android.DeviceOf(process.Command) == device)];
    }

    private Resource Described(string device, IReadOnlyList<RunningProcess> machine, IReadOnlyDictionary<int, Owner> lineage)
    {
        var named = Simulators.OwnerOf(device);
        var raised = machine.Select(process => lineage.GetValueOrDefault(process.Pid)).FirstOrDefault(owner => owner.IsKnown);
        var port = machine.Select(process => Android.PortOf(process.Command)).FirstOrDefault(found => found is not null);
        var first = machine.MinBy(process => process.StartedAt)!;

        return new Resource
        {
            Kind = Name,
            Key = device,
            Title = port is { } console ? $"Android emulator {device} ({Android.Serial(console)})" : $"Android emulator {device}",
            Owner = named.IsKnown ? named : raised,
            Evidence = named.IsKnown ? Evidence.Label : raised.IsKnown ? Evidence.Ancestry : Evidence.None,
            CreatedAt = first.StartedAt,
            Facts = new Dictionary<string, string>
            {
                ["avd"] = device,
                ["pids"] = string.Join(',', machine.Select(process => process.Pid)),
                ["port"] = port is { } known ? $"{known}" : string.Empty
            }
        };
    }

    private async Task<string?> EndedAsync(IReadOnlyList<RunningProcess> running, int? port, CancellationToken cancellationToken)
    {
        if (port is { } console && await android.IsAdbRunningAsync(cancellationToken))
        {
            await programs.RunAsync(android.Adb, ["-s", Android.Serial(console), "emu", "kill"], TimeSpan.FromSeconds(15), cancellationToken);

            if (await AreGoneAsync(running, Grace, cancellationToken))
                return "shut down through adb emu kill";
        }

        foreach (var process in running)
            await programs.RunAsync("kill", ["-TERM", $"{process.Pid}"], TimeSpan.FromSeconds(5), cancellationToken);

        if (await AreGoneAsync(running, Grace, cancellationToken))
            return "shut down on SIGTERM";

        foreach (var process in running)
            await programs.RunAsync("kill", ["-KILL", $"{process.Pid}"], TimeSpan.FromSeconds(5), cancellationToken);

        return await AreGoneAsync(running, TimeSpan.FromSeconds(5), cancellationToken) ? $"killed: it outlived SIGTERM by {Grace.TotalSeconds:0} s" : null;
    }

    private async Task<bool> AreGoneAsync(IReadOnlyList<RunningProcess> running, TimeSpan patience, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + patience;

        while (true)
        {
            table.Refresh();

            var keys = (await table.ListAsync(cancellationToken)).Select(process => process.Key).ToHashSet(StringComparer.Ordinal);

            if (!running.Any(process => keys.Contains(process.Key)))
                return true;

            if (clock.GetUtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(500), clock, cancellationToken);
        }
    }

    /// <summary>
    /// Stops adb's server when the emulator started it and no emulator is left to need it. An emulator starts
    /// the server itself on boot — measured on 2026-10-11, when one started at 21:19:20 under a test emulator
    /// on a Mac that had none — so a server younger than the emulator is the emulator's, and leaves with it.
    /// </summary>
    private async Task<bool> TryStopAdbAsync(IReadOnlyList<RunningProcess> ended, CancellationToken cancellationToken)
    {
        table.Refresh();

        var running = await table.ListAsync(cancellationToken);
        var born = ended.Min(process => process.StartedAt) - TimeSpan.FromSeconds(1);
        var server = running.FirstOrDefault(process => process.Program == "adb" && process.Command.Contains("fork-server server", StringComparison.Ordinal));

        if (server is null || server.StartedAt < born || running.Any(IsMachine))
            return false;

        return (await programs.RunAsync(android.Adb, ["kill-server"], TimeSpan.FromSeconds(10), cancellationToken)).IsSuccess;
    }

    /// <summary>Removes the file an emulator writes for its console port, which outlives every run on that port.</summary>
    /// <remarks>Only when this run wrote it: a file last written before the emulator started is some earlier run's.</remarks>
    private void Forget(int? port, DateTimeOffset started)
    {
        if (port is null)
            return;

        var leftover = Path.Combine(android.Home, $"modem-nv-ram-{port}");

        if (File.Exists(leftover) && File.GetLastWriteTimeUtc(leftover) >= started.UtcDateTime.AddSeconds(-1))
            File.Delete(leftover);
    }
}
