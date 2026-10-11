using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Extensions;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>The server's services as they run on this Mac, with an account directory of the benchmark's own.</summary>
public static class Machine
{
    public static IHost Built(string dataDirectory)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services
            .AddSingleton(new ServerConfig { DataDirectory = dataDirectory, TrashDirectory = Path.Combine(dataDirectory, "Trash") })
            .AddCleanUp();

        return builder.Build();
    }
}
