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
    private const int ReadyWorkers = 64;

    /// <summary>Builds the host the configuration describes.</summary>
    public static IHost Of(string[] args)
    {
        ReadyThreads();

        var settings = ServerConfigExtensions.Settings();
        var config = ServerConfigExtensions.Read(settings);
        var builder = McpHost.CreateBuilder(args);

        builder.ConfigureMcpServer(config);

        return builder.Build();
    }

    /// <summary>Lets the thread pool grow at once to as many workers as a census uses, rather than one or two a second.</summary>
    /// <remarks>
    /// A census runs every kind and trail at once, and many of them walk the disk without awaiting anything, each holding a
    /// worker until it is done. The pool starts with as many workers as the machine has cores and adds the rest slowly, so a
    /// tool call that came in during a census waited seconds for a thread to answer on (7 to 8 s, 2026-11-01). A minimum is
    /// not a reservation: a thread is made only when there is work for it. Sixty-four is twice the 27 kinds and trails a census
    /// runs at once, which leaves room for the tool calls beside it.
    /// </remarks>
    private static void ReadyThreads()
    {
        ThreadPool.GetMinThreads(out var workers, out var completions);
        ThreadPool.SetMinThreads(Math.Max(workers, ReadyWorkers), completions);
    }
}
