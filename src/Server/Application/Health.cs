using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What state the server is in: its build, the session it counts as its own, and what it can see.</summary>
public sealed class Health(ServerConfig config, IHolders sessions, IPrograms programs, Android android, IDockerEngine docker, IChanges changes, Performance performance)
{
    /// <summary>The state of the server.</summary>
    public async Task<Answer> ReportAsync(CancellationToken cancellationToken = default)
    {
        var current = await sessions.CurrentAsync(cancellationToken);
        var known = await sessions.ListAsync(cancellationToken);

        return Answer.Ok(new JsonObject
        {
            ["server"] = new JsonObject
            {
                ["name"] = ThisAssembly.Name,
                ["version"] = ThisAssembly.Version,
                ["transport"] = "stdio",
                ["pid"] = Environment.ProcessId
            },
            ["dataDirectory"] = config.DataDirectory,
            ["session"] = new JsonObject
            {
                ["owner"] = current.ToString(),
                ["isKnown"] = current.IsKnown,
                ["dockerLabel"] = current.IsKnown ? $"{Docker.OwnerLabel}={current}" : null,
                ["source"] = File.Exists(Path.Combine(config.SleepOfDirectory, SleepOfAgents.FileName)) ? "Snail.SleepOf" : "process table"
            },
            ["sessions"] = new JsonObject
            {
                ["alive"] = known.Count(session => session.IsAlive),
                ["ended"] = known.Count(session => !session.IsAlive)
            },
            ["reach"] = new JsonObject
            {
                ["simctl"] = (await programs.ReadAsync("xcrun", ["simctl", "help"], TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10), cancellationToken)).IsSuccess,
                ["emulator"] = File.Exists(android.Emulator),
                ["adb"] = File.Exists(android.Adb) ? (await android.IsAdbRunningAsync(cancellationToken) ? "server running" : "installed, server not running") : "not installed",
                ["docker"] = await docker.UnreachableAsync(cancellationToken) ?? "daemon running"
            },
            ["disk"] = Disk.Report(),
            ["performance"] = performance.Report(),
            ["heard"] = new JsonArray([.. changes.Heard.Select(said => (JsonNode?)said)])
        });
    }
}
