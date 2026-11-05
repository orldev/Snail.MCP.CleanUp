using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>Composition root: lists the features to wire; how each is built is known one level down.</summary>
public static class McpServerRegistrationExtensions
{
    /// <summary>How many workers the thread pool may grow to at once: twice the 27 kinds and trails a census runs side by side.</summary>
    /// <remarks>
    /// A census runs every kind and trail at once, and many of them walk the disk without awaiting anything, each holding a
    /// worker until it is done. The pool starts with as many workers as the machine has cores and adds the rest slowly, so a
    /// tool call that came in during a census waited seconds for a thread to answer on (7 to 8 s, 2026-11-01). A minimum is
    /// not a reservation: a thread is made only when there is work for it, and the doubling leaves room for the tool calls
    /// beside a census.
    /// </remarks>
    internal const int ReadyWorkers = 64;

    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Configures the server to speak stdio to the process that started it.</summary>
        public IHostApplicationBuilder ConfigureMcpServer(ServerConfig config)
        {
            builder.Services
                .AddSingleton(config)
                .AddReadyThreads(ReadyWorkers)
                .AddCleanUp()
                .AddMcpTransport();

            return builder;
        }
    }
}
