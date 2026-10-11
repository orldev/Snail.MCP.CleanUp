using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>Composition root: lists the features to wire; how each is built is known one level down.</summary>
public static class McpServerRegistrationExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>Configures the server to speak stdio to the process that started it.</summary>
        public IHostApplicationBuilder ConfigureMcpServer(ServerConfig config)
        {
            builder.UseStderrLogging();

            builder.Services
                .AddSingleton(config)
                .AddCleanUp()
                .AddMcpTransport();

            return builder;
        }

        /// <summary>stdio carries JSON-RPC, so every log line must go to stderr.</summary>
        internal IHostApplicationBuilder UseStderrLogging()
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            return builder;
        }
    }
}
