using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Tools;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>Driving adapter: the tools reach the model over stdio.</summary>
public static class TransportRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>The server speaking stdio to the process that started it.</summary>
        public IServiceCollection AddMcpTransport()
        {
            services
                .AddMcpServer(server =>
                {
                    server.ServerInfo = ServerIdentity.Of(typeof(ThisAssembly).Assembly);
                    server.ServerInstructions = ServerGuidance.Instructions;
                })
                .WithStdioServerTransport()
                .WithTools<DiagnosticsTools>()
                .WithTools<RunTools>()
                .WithTools<LeaseTools>()
                .WithTools<AccountTools>()
                .WithTools<DiscoveryTools>()
                .WithPrompts<CleanupPrompts>()
                .WithRequestFilters(filters => filters.AddCallToolFilter(Patient));

            services.AddSingleton<Patience>();
            services.AddHostedService<HookSocket>();

            return services;
        }
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
