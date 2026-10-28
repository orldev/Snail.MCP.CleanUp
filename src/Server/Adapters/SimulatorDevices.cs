using System.Net;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The iOS simulators of this Mac, listed without starting the service that runs them.</summary>
/// <remarks>
/// <c>simctl list</c> starts CoreSimulatorService when it is not running, and every census asks. On a Mac whose owner no
/// longer used the simulator, the service and its helpers stayed up for 17 days started by nothing but this server
/// (2026-10-28). While the service is down no simulator runs, and none is made or deleted without starting it, so the
/// devices are read from their folders instead: a device is a folder of the device set named by its UDID that holds a
/// device.plist. A folder without one is the remains of a device simctl no longer lists, and is not a device.
/// </remarks>
public sealed class SimulatorDevices(IPrograms programs, IProcessTable table)
{
    /// <summary>The program that runs the simulators, as the process table names it.</summary>
    public const string Service = "com.apple.CoreSimulator.CoreSimulatorService";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PlistPatience = TimeSpan.FromSeconds(10);

    /// <summary>XML rather than JSON: plutil cannot write a date as JSON, and a device.plist keeps when it was last booted.</summary>
    private const string PlistFormat = "xml1";

    /// <summary>The folder of the device set read while the service is down.</summary>
    public string DeviceSet { get; init; } = MacPlaces.SimulatorDeviceSet;

    /// <summary>The devices, and why they could not be listed when they could not.</summary>
    public sealed record Listing(IReadOnlyList<Simulators.Device> Devices, string? Failure)
    {
        /// <summary>Whether the devices could be listed.</summary>
        public bool IsSuccess => Failure is null;
    }

    /// <summary>Every device: from simctl while the service runs, from the device set's folders while it does not.</summary>
    public async Task<Listing> ListAsync(CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);

        if (!IsServiceRunning(running))
            return new Listing(await FromFoldersAsync(cancellationToken), null);

        var run = await programs.ReadAsync("xcrun", ["simctl", "list", "devices", "-j"], IPrograms.Census, Patience, cancellationToken);

        return run.IsSuccess ? new Listing(Simulators.Parse(run.Output), null) : new Listing([], run.Failure);
    }

    /// <summary>Whether CoreSimulatorService is among the running programs.</summary>
    public static bool IsServiceRunning(IReadOnlyList<RunningProcess> running) =>
        running.Any(process => string.Equals(Path.GetFileName(process.Program), Service, StringComparison.Ordinal));

    private async Task<IReadOnlyList<Simulators.Device>> FromFoldersAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(DeviceSet))
            return [];

        var devices = new List<Simulators.Device>();

        foreach (var folder in Directory.EnumerateDirectories(DeviceSet))
        {
            var udid = Path.GetFileName(folder);
            var plist = Path.Combine(folder, "device.plist");

            if (!Simulators.IsUdid(udid) || !File.Exists(plist))
                continue;

            var read = await programs.ReadAsync("plutil", ["-convert", PlistFormat, "-o", "-", plist], IPrograms.Census, PlistPatience, cancellationToken);

            if (read.IsSuccess && FromPlist(read.Output, udid, folder) is { } device)
                devices.Add(device);
        }

        return devices;
    }

    /// <summary>A device as its device.plist describes it, shut down because the service that boots it is not running; null for one marked deleted.</summary>
    public static Simulators.Device? FromPlist(string xml, string udid, string folder)
    {
        if (Value(xml, "isDeleted") == "true")
            return null;

        var name = Value(xml, "name") ?? udid;
        var runtime = Value(xml, "runtime") ?? "unknown";

        return new Simulators.Device(udid, name, "Shutdown", runtime, Path.Combine(folder, "data"), IsAvailable: true);
    }

    /// <summary>The value of a key at the top of an XML property list: the text of a string or an integer, or true and false.</summary>
    private static string? Value(string xml, string key)
    {
        var marker = $"<key>{key}</key>";
        var at = xml.IndexOf(marker, StringComparison.Ordinal);

        if (at < 0)
            return null;

        var rest = xml.AsSpan(at + marker.Length).TrimStart();

        if (rest.StartsWith("<true/>", StringComparison.Ordinal))
            return "true";

        if (rest.StartsWith("<false/>", StringComparison.Ordinal))
            return "false";

        foreach (var tag in (ReadOnlySpan<string>)["string", "integer"])
        {
            var open = $"<{tag}>";

            if (!rest.StartsWith(open, StringComparison.Ordinal))
                continue;

            var text = rest[open.Length..];
            var end = text.IndexOf($"</{tag}>", StringComparison.Ordinal);

            return end < 0 ? null : WebUtility.HtmlDecode(text[..end].ToString());
        }

        return null;
    }
}
