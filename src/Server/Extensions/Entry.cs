using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>What the program is asked to be: the MCP server, or one of the verbs Claude Code's hooks and a person call.</summary>
public static class Entry
{
    /// <summary>The verbs besides serving: a hook, a detached census, and putting the hooks in or out.</summary>
    public static IReadOnlyList<string> Verbs { get; } = [Hooks.Verb, Hooks.CensusVerb, "install-hooks", "uninstall-hooks"];

    /// <summary>Runs whichever the arguments ask for; serving MCP when they ask for nothing else.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is not [var verb, ..] || !Verbs.Contains(verb))
        {
            await McpServerHost.Of(args).RunAsync();

            return 0;
        }

        using var host = Verbed();
        var services = host.Services;

        switch (verb)
        {
            case Hooks.Verb:
                await services.GetRequiredService<Hooks>().HandleAsync(await Console.In.ReadToEndAsync(), CancellationToken.None);
                break;
            case Hooks.CensusVerb:
                await services.GetRequiredService<Hooks>().CensusAsync(CancellationToken.None);
                break;
            case "install-hooks":
                await Console.Out.WriteLineAsync(services.GetRequiredService<HookInstallation>().Install(Invocation.CommandLine(Hooks.Verb)));
                break;
            default:
                await Console.Out.WriteLineAsync(services.GetRequiredService<HookInstallation>().Uninstall());
                break;
        }

        return 0;
    }

    /// <summary>The services without a transport: a verb answers once and exits.</summary>
    private static IHost Verbed()
    {
        var config = ServerConfigExtensions.Read(ServerConfigExtensions.Settings());
        var builder = Host.CreateApplicationBuilder();

        builder.UseStderrLogging();
        builder.Services.AddSingleton(config).AddCleanUp();

        return builder.Build();
    }
}
