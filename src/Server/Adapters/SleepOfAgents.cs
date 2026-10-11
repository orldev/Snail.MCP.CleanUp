using System.Text.Json;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The sessions Snail.SleepOf has written down in its <c>agents.json</c>, as they are written.</summary>
/// <remarks>
/// SleepOf hears every hook of every Claude Code session on the machine and maps each session to the
/// process of its agent. That map is the one thing a process table cannot give: which session a process
/// is. Its dates are seconds since the first of January 2001, the epoch of Apple's own date type.
/// </remarks>
public static class SleepOfAgents
{
    /// <summary>The name of the file in SleepOf's directory.</summary>
    public const string FileName = "agents.json";

    private static readonly DateTimeOffset AppleEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>One session as SleepOf knows it.</summary>
    public sealed record Entry(string Id, int? Pid, DateTimeOffset? StartedAt, string? Directory, string? State);

    /// <summary>The sessions in the text of an <c>agents.json</c>; none when the text is not one.</summary>
    public static IReadOnlyList<Entry> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<Entry> Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sessions", out var sessions) || sessions.ValueKind != JsonValueKind.Array)
            return [];

        var pids = Pids(root);

        return
        [
            .. sessions.EnumerateArray()
                .Where(session => session.ValueKind == JsonValueKind.Object && Text(session, "id") is not null)
                .Select(session => new Entry(
                    Text(session, "id")!,
                    pids.GetValueOrDefault(Text(session, "id")!),
                    Moment(session, "startedAt"),
                    Text(session, "cwd"),
                    Text(session, "state")))
        ];
    }

    private static Dictionary<string, int?> Pids(JsonElement root)
    {
        var pids = new Dictionary<string, int?>(StringComparer.Ordinal);

        if (!root.TryGetProperty("pids", out var map) || map.ValueKind != JsonValueKind.Object)
            return pids;

        foreach (var pair in map.EnumerateObject())
        {
            if (pair.Value.ValueKind == JsonValueKind.Number && pair.Value.TryGetInt32(out var pid))
                pids[pair.Name] = pid;
        }

        return pids;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Moment(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? AppleEpoch.AddSeconds(value.GetDouble())
            : null;
}
