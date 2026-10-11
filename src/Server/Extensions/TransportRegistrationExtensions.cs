using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Tools;
using Snail.MCP.CleanUp.Tools.Prompts;

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
                    server.ServerInfo = new Implementation { Name = ThisAssembly.Name, Version = ThisAssembly.Version };
                    server.ServerInstructions = ServerGuidance.Instructions;
                })
                .WithStdioServerTransport()
                .WithTools<DiagnosticsTools>();

            return services;
        }
    }
}
