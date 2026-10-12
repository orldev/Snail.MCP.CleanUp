using System.Text.Json;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Writes this server's hooks into Claude Code's settings beside everybody else's, and takes them out again.</summary>
/// <remarks>
/// The settings file is shared: Snail.SleepOf, memory servers and the person all keep hooks in it. So this
/// server touches only its own entries — found by its own command line — leaves every other one exactly as
/// it was, keeps a copy of the file as it found it, and writes the new one whole or not at all.
/// </remarks>
public sealed class HookInstallation(ServerConfig config)
{
    /// <summary>The hook events this server answers, with how long each may take.</summary>
    public static IReadOnlyDictionary<string, int> Events { get; } = new Dictionary<string, int>
    {
        ["SessionStart"] = 30,
        ["Stop"] = 10,
        ["SessionEnd"] = 120
    };

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true };

    /// <summary>Puts this server's hooks in, replacing any it put in before.</summary>
    public string Install(string command)
    {
        var settings = Read();
        var hooks = settings["hooks"] as JsonObject ?? [];

        Without(hooks);

        foreach (var (name, timeout) in Events)
        {
            var entries = hooks[name] as JsonArray ?? [];

            entries.Add((JsonNode)new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = timeout })
            });

            hooks[name] = entries;
        }

        settings["hooks"] = hooks;
        Write(settings);

        return $"installed {string.Join(", ", Events.Keys)} in {config.ClaudeSettings}";
    }

    /// <summary>Takes this server's hooks out, leaving every other one.</summary>
    public string Uninstall()
    {
        var settings = Read();

        if (settings["hooks"] is JsonObject hooks)
            Without(hooks);

        Write(settings);

        return $"removed this server's hooks from {config.ClaudeSettings}";
    }

    /// <summary>Whether a hook entry is this server's own, told by the program it runs.</summary>
    public static bool IsOwn(JsonNode? entry) =>
        entry?["hooks"] is JsonArray commands
        && commands.Any(command => command?["command"]?.GetValue<string>() is { } line
            && (line.Contains("Snail.MCP.CleanUp", StringComparison.Ordinal) || line.Contains("snail-mcp-cleanup", StringComparison.Ordinal)));

    private static void Without(JsonObject hooks)
    {
        foreach (var (name, entries) in hooks.ToList())
        {
            if (entries is not JsonArray list)
                continue;

            foreach (var own in list.Where(IsOwn).ToList())
                list.Remove(own);

            if (list.Count == 0)
                hooks.Remove(name);
        }
    }

    private JsonObject Read()
    {
        if (!File.Exists(config.ClaudeSettings))
            return [];

        var text = File.ReadAllText(config.ClaudeSettings);

        File.WriteAllText($"{config.ClaudeSettings}.cleanup-backup", text);

        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException($"{config.ClaudeSettings} is not a JSON object");
    }

    private void Write(JsonObject settings)
    {
        var temporary = $"{config.ClaudeSettings}.{Guid.NewGuid():n}";

        Directory.CreateDirectory(Path.GetDirectoryName(config.ClaudeSettings)!);
        File.WriteAllText(temporary, settings.ToJsonString(Written));
        File.Move(temporary, config.ClaudeSettings, overwrite: true);
    }
}
