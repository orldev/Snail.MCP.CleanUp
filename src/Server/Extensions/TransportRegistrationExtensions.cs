using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Tools;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Discovery;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>Driving adapter: the tools reach the model over stdio.</summary>
public static class TransportRegistrationExtensions
{
    /// <summary>The words a caller reaches for that a tool is not named with, by the tool's name; read only when someone searches.</summary>
    /// <remarks>A guard test keeps these the tools the server lists, since nothing in the compiler notices a tool renamed or added.</remarks>
    internal static IReadOnlyDictionary<string, string> Meanings { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cleanup_inventory"] = "list show what left leftovers garbage junk running orphans disk space full port ports occupied status",
        ["cleanup_plan"] = "clean cleanup tidy remove delete free decide preview dry run what would",
        ["cleanup_apply"] = "execute carry confirm go ahead remove delete kill stop do it",
        ["cleanup_release"] = "release stop kill end delete shutdown free port simulator emulator one",
        ["cleanup_keep"] = "keep pin protect spare exclude leave alone ignore preserve",
        ["cleanup_learn"] = "learn rule teach new kind discovery unknown pattern classify",
        ["cleanup_run"] = "start launch run background server dev watcher worker daemon npm serve",
        ["cleanup_simulator_acquire"] = "ios iphone simulator device xcode simctl test boot own clone",
        ["cleanup_emulator_acquire"] = "android emulator avd device adb phone test boot own",
        ["cleanup_diagnose"] = "health status broken why session which tools available docker adb simctl",
        ["find_tool"] = "search discover name parameters arguments how which"
    };

    extension(IServiceCollection services)
    {
        /// <summary>The server speaking stdio to the process that started it.</summary>
        /// <remarks>
        /// A tool that throws answers in the envelope (<c>ExplainFailures</c>); <c>find_tool</c> is Snail.Sdk.Mcp's, over the
        /// tools registered here and the words of <see cref="Meanings"/>.
        /// </remarks>
        public IServiceCollection AddMcpTransport()
        {
            services
                .AddMcpServer(server =>
                {
                    server.ServerInfo = ServerIdentity.Of(typeof(TransportRegistrationExtensions).Assembly);
                    server.ServerInstructions = ServerGuidance.Instructions;
                })
                .ExplainFailures()
                .WithStdioServerTransport()
                .WithTools<DiagnosticsTools>()
                .WithTools<RunTools>()
                .WithTools<LeaseTools>()
                .WithTools<AccountTools>()
                .WithToolDiscovery(Discovery)
                .WithArgumentCheck()
                .WithJobs("cleanup")
                .WithPrompts<CleanupPrompts>()
                .WithRequestFilters(filters => filters.AddCallToolFilter(Patient));

            services.AddSingleton<Patience>();
            services.AddHostedService<HookSocket>();
            services.AddHostedService<PoolWatch>();

            return services;
        }
    }

    /// <summary>How <c>find_tool</c> reads this server's tools: the prefix every one of its own shares, and the other words each is asked for by.</summary>
    internal static void Discovery(DiscoveryOptions options)
    {
        options.Prefix = "cleanup_";

        foreach (var (tool, words) in Meanings)
            options.Meanings[tool] = words;
    }

    /// <summary>Every tool call answered within its tool's patience, and logged when it was slow.</summary>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> Patient(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.Services?.GetService<Patience>() is not { } patience)
                return await next(context, cancellationToken);

            return await patience.AnswerAsync(context.Params?.Name ?? "a tool", ended => next(context, ended).AsTask(), cancellationToken);
        };
}
