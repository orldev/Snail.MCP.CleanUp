using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>The server as a running host: a child process speaking stdio to the agent that started it.</summary>
/// <remarks>
/// There is no address to listen at. What this server cleans up lives on the machine the agent works on,
/// and a server elsewhere would see neither the processes of that machine nor its simulators.
/// <para>
/// The host is the empty one: the server reads its settings itself (<see cref="ServerConfigExtensions"/>), so the
/// appsettings files, their watchers, the command line and the default loggers of the full builder were all loaded
/// for nothing.
/// </para>
/// </remarks>
public static class McpServerHost
{
    /// <summary>Builds the host the configuration describes.</summary>
    public static IHost Of(string[] args)
    {
        var settings = ServerConfigExtensions.Settings();
        var config = ServerConfigExtensions.Read(settings);
        var builder = McpHost.CreateBuilder(args);

        builder.ConfigureMcpServer(config);

        return builder.Build();
    }
}
