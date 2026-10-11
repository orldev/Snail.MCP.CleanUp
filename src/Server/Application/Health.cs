using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Application;

/// <summary>What state the server is in: its build, how it is reached and where it keeps its account.</summary>
public sealed class Health(ServerConfig config)
{
    /// <summary>The state of the server.</summary>
    public Task<Answer> ReportAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Answer.Ok(new JsonObject
        {
            ["server"] = new JsonObject
            {
                ["name"] = ThisAssembly.Name,
                ["version"] = ThisAssembly.Version,
                ["transport"] = "stdio"
            },
            ["dataDirectory"] = config.DataDirectory
        }));
}
