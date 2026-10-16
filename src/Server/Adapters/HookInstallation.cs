using System.Text.Json;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Writes this server's hooks into Claude Code's settings beside everybody else's, and takes them out again.</summary>
/// <remarks>
/// The settings file is shared: Snail.SleepOf, memory servers and the person all keep hooks in it, and its <c>env</c> may
/// hold their keys. So this server touches only its own commands — its programs run with the hook verb — leaves every
/// other command exactly as it was, even one in the same entry, keeps a copy of the file as it found it, writes the new
/// one whole or not at all, and gives both the mode the file had: a file only its owner could read came back readable by
/// everyone, and a person's own command in a folder named after this server was taken out (2026-10-16).
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

    /// <summary>The names a hook line of this server carries: its package, its command and the hook forwarder, now and before the s3l names.</summary>
    private static readonly string[] OwnNames = ["Snail.MCP.CleanUp", "s3l-mcp-cleanup", "snail-mcp-cleanup"];

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

    /// <summary>Whether a hook command is this server's own: one of its programs, run with the hook verb.</summary>
    public static bool IsOwn(JsonNode? command) =>
        command?["command"] is JsonValue value
        && value.TryGetValue(out string? line)
        && line.EndsWith($" {Hooks.Verb}", StringComparison.Ordinal)
        && OwnNames.Any(name => line.Contains(name, StringComparison.Ordinal));

    private static void Without(JsonObject hooks)
    {
        foreach (var (name, entries) in hooks.ToList())
        {
            if (entries is not JsonArray list)
                continue;

            foreach (var entry in list.OfType<JsonObject>().ToList())
                WithoutOwnCommands(entry, list);

            if (list.Count == 0)
                hooks.Remove(name);
        }
    }

    /// <summary>Takes this server's commands out of an entry, and the entry out of its list once no command is left in it.</summary>
    private static void WithoutOwnCommands(JsonObject entry, JsonArray list)
    {
        if (entry["hooks"] is not JsonArray commands)
            return;

        foreach (var own in commands.Where(IsOwn).ToList())
            commands.Remove(own);

        if (commands.Count == 0)
            list.Remove(entry);
    }

    private JsonObject Read()
    {
        if (!File.Exists(config.ClaudeSettings))
            return [];

        var text = File.ReadAllText(config.ClaudeSettings);

        Replace($"{config.ClaudeSettings}.cleanup-backup", text, ModeOf(config.ClaudeSettings));

        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException($"{config.ClaudeSettings} is not a JSON object");
    }

    private void Write(JsonObject settings)
    {
        var mode = ModeOf(config.ClaudeSettings);

        Directory.CreateDirectory(Path.GetDirectoryName(config.ClaudeSettings)!);
        Replace(config.ClaudeSettings, settings.ToJsonString(Written), mode);
    }

    /// <summary>The mode a rewritten file keeps: the one it has, or its owner's alone for a file not there yet; none where files have no such mode.</summary>
    private static UnixFileMode? ModeOf(string path)
    {
        if (OperatingSystem.IsWindows())
            return null;

        return File.Exists(path) ? File.GetUnixFileMode(path) : UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    /// <summary>Puts the text in place of the file whole, created with the mode given so that nobody else reads it even for a moment.</summary>
    private static void Replace(string path, string text, UnixFileMode? mode)
    {
        var temporary = $"{path}.{Guid.NewGuid():n}";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = mode;

        using (var stream = new FileStream(temporary, options))
        using (var writer = new StreamWriter(stream))
            writer.Write(text);

        File.Move(temporary, path, overwrite: true);
    }
}
