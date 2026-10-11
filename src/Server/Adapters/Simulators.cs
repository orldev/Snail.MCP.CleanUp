using System.Text.Json;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The iOS simulators of this Mac as <c>simctl</c> lists them, and the names that say whose one is.</summary>
public static class Simulators
{
    /// <summary>What every simulator made for an agent is called before its owner.</summary>
    public const string Prefix = "snail-";

    /// <summary>One simulator device.</summary>
    public sealed record Device(string Udid, string Name, string State, string Runtime, string? DataPath, bool IsAvailable)
    {
        /// <summary>Whether it is running.</summary>
        public bool IsBooted => string.Equals(State, "Booted", StringComparison.Ordinal);
    }

    /// <summary>Every device in <c>simctl list devices -j</c>, of every runtime.</summary>
    public static IReadOnlyList<Device> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("devices", out var runtimes) || runtimes.ValueKind != JsonValueKind.Object)
                return [];

            return
            [
                .. runtimes.EnumerateObject().SelectMany(runtime => runtime.Value.EnumerateArray().Select(device => new Device(
                    device.GetProperty("udid").GetString()!,
                    device.GetProperty("name").GetString()!,
                    device.TryGetProperty("state", out var state) ? state.GetString() ?? "Unknown" : "Unknown",
                    runtime.Name,
                    device.TryGetProperty("dataPath", out var data) ? data.GetString() : null,
                    !device.TryGetProperty("isAvailable", out var available) || available.GetBoolean())))
            ];
        }
        catch (Exception unreadable) when (unreadable is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>A kind of device simctl can make, such as an iPhone 17 Pro.</summary>
    public sealed record DeviceType(string Name, string Identifier, string? Family);

    /// <summary>A runtime simctl can run a device on, such as iOS 27.0.</summary>
    public sealed record Runtime(string Identifier, string Platform, string Version, bool IsAvailable);

    /// <summary>The device types in <c>simctl list devicetypes -j</c>.</summary>
    public static IReadOnlyList<DeviceType> DeviceTypes(string json) =>
        Read(json, "devicetypes", element => new DeviceType(
            element.GetProperty("name").GetString()!,
            element.GetProperty("identifier").GetString()!,
            element.TryGetProperty("productFamily", out var family) ? family.GetString() : null));

    /// <summary>The runtimes in <c>simctl list runtimes -j</c>.</summary>
    public static IReadOnlyList<Runtime> Runtimes(string json) =>
        Read(json, "runtimes", element => new Runtime(
            element.GetProperty("identifier").GetString()!,
            element.TryGetProperty("platform", out var platform) ? platform.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("version", out var version) ? version.GetString() ?? string.Empty : string.Empty,
            !element.TryGetProperty("isAvailable", out var available) || available.GetBoolean()));

    private static IReadOnlyList<T> Read<T>(string json, string list, Func<JsonElement, T> item)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.TryGetProperty(list, out var items) && items.ValueKind == JsonValueKind.Array
                ? [.. items.EnumerateArray().Select(item)]
                : [];
        }
        catch (Exception unreadable) when (unreadable is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>The number in a name this server gave; 0 for any other name.</summary>
    public static int NumberOf(string name) =>
        name.LastIndexOf('-') is var dash and > 0 && int.TryParse(name[(dash + 1)..], out var number) ? number : 0;

    /// <summary>The name a device made for an owner gets: its owner and a number, so that it can never be mistaken for anyone else's.</summary>
    /// <remarks>
    /// Written with the characters every device name allows. An Android virtual device may be named only with
    /// letters, digits, dots, dashes and underscores, and refuses to start with the <c>@</c> of a session known
    /// by its process — so <c>@</c> is written <c>_</c>, which no session id contains, and read back the same way.
    /// </remarks>
    public static string NameFor(Owner owner, int number) => $"{Prefix}{owner.Id.Replace('@', '_')}-{number}";

    /// <summary>The owner a simulator's name carries; nobody when the name is not one this server gives.</summary>
    public static Owner OwnerOf(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            return Owner.Nobody;

        var dash = name.LastIndexOf('-');

        return dash > Prefix.Length && int.TryParse(name[(dash + 1)..], out _) ? Owner.Session(name[Prefix.Length..dash].Replace('_', '@')) : Owner.Nobody;
    }

    /// <summary>A runtime identifier as people say it: <c>com.apple.CoreSimulator.SimRuntime.iOS-27-0</c> is iOS 27.0.</summary>
    public static string Spoken(string runtime)
    {
        var parts = runtime[(runtime.LastIndexOf('.') + 1)..].Split('-');

        return parts.Length > 1 ? $"{parts[0]} {string.Join('.', parts[1..])}" : runtime;
    }

    /// <summary>Whether a text is a device's UDID, the only thing a simulator may be addressed by when it is shut down or deleted.</summary>
    /// <remarks>
    /// <c>simctl shutdown all</c> once stopped another agent's test run in the middle. Addressing by UDID alone
    /// makes that call impossible to write by mistake: <c>all</c>, <c>booted</c> and a device name are not UDIDs.
    /// </remarks>
    public static bool IsUdid(string text) => Guid.TryParseExact(text, "D", out _);
}
