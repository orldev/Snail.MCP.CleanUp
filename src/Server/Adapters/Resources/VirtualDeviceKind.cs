using Snail.MCP.CleanUp.Adapters.Trails;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Android virtual device definitions; the ones named for a session were made for it and leave with it.</summary>
/// <remarks>
/// A device made for an agent can be made again from the same system image, so it is erased rather than
/// moved to the Trash: its data is gigabytes, and gigabytes in the Trash are still gigabytes on the disk.
/// A device is never removed while an emulator runs it.
/// </remarks>
public sealed class VirtualDeviceKind(Android android, IProcessTable table) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.VirtualDevice;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(android.Devices))
            return new Observation(Name, [], []);

        var running = (await table.ListAsync(cancellationToken)).Where(process => Android.DeviceOf(process.Command) is not null).ToList();

        return new Observation(
            Name,
            [.. android.DeviceNames().Select(name => Described(name, running))],
            [.. android.DeviceNames().SelectMany(name => new[] { $"file:{Path.Combine(android.Devices, $"{name}.ini")}", $"file:{Path.Combine(android.Devices, $"{name}.avd")}" })]);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var definition = Path.Combine(android.Devices, $"{resource.Key}.ini");
        var directory = Path.Combine(android.Devices, $"{resource.Key}.avd");

        if (!File.Exists(definition) && !Directory.Exists(directory))
            return Release.AlreadyGone(resource);

        table.Refresh();

        if ((await table.ListAsync(cancellationToken)).Any(process => Android.DeviceOf(process.Command) == resource.Key))
            return Release.Refused(resource, $"an emulator is running {resource.Key}");

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        File.Delete(definition);

        return Release.Done(resource, $"erased the virtual device {resource.Key}: it can be made again from its system image");
    }

    private Resource Described(string name, IReadOnlyList<RunningProcess> running)
    {
        var owner = Simulators.OwnerOf(name);
        var isRunning = running.Any(process => Android.DeviceOf(process.Command) == name);

        return new Resource
        {
            Kind = Name,
            Key = name,
            Title = $"Android virtual device {name}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            IsRegenerable = owner.IsKnown,
            Bytes = Sizes.Of(Path.Combine(android.Devices, $"{name}.avd")),
            After = isRunning ? [$"{Kinds.Emulator}:{name}"] : []
        };
    }
}
