
namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>iOS simulators: the ones made for an agent are its own by name; every other one is somebody else's.</summary>
/// <remarks>
/// A simulator is shared by nature — a developer keeps theirs for months, and a second agent may be driving
/// one right now. So a simulator is released only when its name or the ledger proves whose it is, by its
/// UDID alone, and only after the programs driving it are stopped: a simulator shut down under a running
/// test runner leaves the runner hanging instead of ending.
/// </remarks>
public sealed class SimulatorKind(IPrograms programs, IProcessTable table, Sizes sizes, TimeProvider clock) : IResourceKind
{
    /// <summary>How long a simulator may take to shut down or to be deleted.</summary>
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc/>
    public string Name => Kinds.Simulator;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var run = await programs.ReadAsync("xcrun", ["simctl", "list", "devices", "-j"], IPrograms.Census, Patience, cancellationToken);

        if (!run.IsSuccess)
            return Observation.Unseen(Name, $"simctl could not list the simulators: {run.Failure}");

        var running = await table.ListAsync(cancellationToken);
        var devices = Simulators.Parse(run.Output);

        return new Observation(
            Name,
            [.. devices.Select(device => Described(device, running))],
            [.. devices.Where(device => device.DataPath is not null).Select(device => $"file:{Path.GetDirectoryName(device.DataPath)}")]);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var udid = resource.Key;

        if (!Simulators.IsUdid(udid))
            return Release.Refused(resource, $"{udid} is not a UDID, and a simulator is never addressed any other way");

        var listed = await programs.RunAsync("xcrun", ["simctl", "list", "devices", "-j"], Patience, cancellationToken);
        var device = Simulators.Parse(listed.Output).FirstOrDefault(candidate => candidate.Udid == udid);

        if (device is null)
            return Release.AlreadyGone(resource);

        if (resource.Facts.TryGetValue("name", out var name) && name != device.Name)
            return Release.NotTheSame(resource, $"{udid} is now called {device.Name}");

        var stopped = await StopDriversAsync(udid, cancellationToken);

        if (device.IsBooted)
        {
            var shutdown = await programs.RunAsync("xcrun", ["simctl", "shutdown", udid], Patience, cancellationToken);

            if (!shutdown.IsSuccess && !shutdown.Error.Contains("current state: Shutdown", StringComparison.Ordinal))
                return Release.Refused(resource, $"could not shut it down: {shutdown.Failure}");
        }

        var deleted = await programs.RunAsync("xcrun", ["simctl", "delete", udid], Patience, cancellationToken);

        if (!deleted.IsSuccess)
            return Release.Refused(resource, $"could not delete it: {deleted.Failure}");

        var drivers = stopped > 0 ? $"; {stopped} program(s) driving it were stopped first" : string.Empty;

        return Release.Done(resource, $"deleted {device.Name}{(device.IsBooted ? " after shutting it down" : string.Empty)}{drivers}");
    }

    private Resource Described(Simulators.Device device, IReadOnlyList<RunningProcess> running)
    {
        var owner = Simulators.OwnerOf(device.Name);
        var drivers = Drivers(device.Udid, running);

        return new Resource
        {
            Kind = Name,
            Key = device.Udid,
            Title = $"{device.Name} ({Simulators.Spoken(device.Runtime)}, {device.State.ToLowerInvariant()})",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            Bytes = device.DataPath is null ? null : sizes.Of(Path.GetDirectoryName(device.DataPath)!),
            Blocker = device.IsAvailable ? null : "its runtime is no longer installed; simctl can still delete it by UDID",
            After = [.. drivers.Select(driver => $"{Kinds.Process}:{driver.Key}")],
            Facts = new Dictionary<string, string>
            {
                ["name"] = device.Name,
                ["state"] = device.State,
                ["drivers"] = string.Join(',', drivers.Select(driver => driver.Pid))
            }
        };
    }

    /// <summary>The programs whose command line names the simulator: test runners, video recordings, log streams.</summary>
    private static IReadOnlyList<RunningProcess> Drivers(string udid, IReadOnlyList<RunningProcess> running) =>
        [.. running.Where(process => process.Command.Contains(udid, StringComparison.OrdinalIgnoreCase) && !process.Command.Contains("CoreSimulator", StringComparison.Ordinal))];

    private async Task<int> StopDriversAsync(string udid, CancellationToken cancellationToken)
    {
        table.Refresh();

        var drivers = Drivers(udid, await table.ListAsync(cancellationToken));

        foreach (var driver in drivers)
            await programs.RunAsync("kill", ["-TERM", $"{driver.Pid}"], TimeSpan.FromSeconds(5), cancellationToken);

        if (drivers.Count > 0)
            await Task.Delay(TimeSpan.FromSeconds(2), clock, cancellationToken);

        return drivers.Count;
    }
}
