using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What state the server is in: its build, the session it counts as its own, and what it can see.</summary>
public sealed class Health(ServerConfig config, ISessions sessions)
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
                ["transport"] = "stdio"
            },
            ["dataDirectory"] = config.DataDirectory,
            ["session"] = new JsonObject
            {
                ["owner"] = current.ToString(),
                ["isKnown"] = current.IsKnown,
                ["source"] = File.Exists(Path.Combine(config.SleepOfDirectory, SleepOfAgents.FileName)) ? "Snail.SleepOf" : "process table"
            },
            ["sessions"] = new JsonObject
            {
                ["alive"] = known.Count(session => session.IsAlive),
                ["ended"] = known.Count(session => !session.IsAlive)
            }
        });
    }
}
