using Microsoft.Extensions.DependencyInjection;
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
                .WithTools<DiscoveryTools>();

            services.AddHostedService<HookSocket>();

            return services;
        }
    }
}
