using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.Sdk.Mcp.Hosting;

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

        await using var services = Verbed();

        switch (verb)
        {
            case Hooks.Verb:
                await services.GetRequiredService<Hooks>().HandleAsync(await Console.In.ReadToEndAsync(), CancellationToken.None);
                break;
            case Hooks.CensusVerb:
                await services.GetRequiredService<Hooks>().CensusAsync(CancellationToken.None);
                break;
            case "install-hooks" when args is [_, "--forwarder", var forwarder, ..]:
                if (!Path.IsPathFullyQualified(forwarder) || !File.Exists(forwarder))
                {
                    await Console.Error.WriteLineAsync($"the forwarder must be the full path of the published snail-mcp-cleanup-hook, not '{forwarder}'");

                    return 1;
                }

                var socket = services.GetRequiredService<ServerConfig>().HookSocket;

                await Console.Out.WriteLineAsync(services.GetRequiredService<HookInstallation>().Install(Application.Invocation.Forwarded(forwarder, socket, Hooks.Verb)));
                break;
            case "install-hooks":
                await Console.Out.WriteLineAsync(services.GetRequiredService<HookInstallation>().Install(Application.Invocation.CommandLine(Hooks.Verb)));
                break;
            default:
                await Console.Out.WriteLineAsync(services.GetRequiredService<HookInstallation>().Uninstall());
                break;
        }

        return 0;
    }

    /// <summary>The services without a transport or a host: a verb answers once and exits.</summary>
    /// <remarks>
    /// A hook is a process of its own every time Claude Code fires one, so whatever it builds before its work is
    /// paid at every session start and every stop. It has nothing to host — no lifetime to wait for, no background
    /// census to run — so it builds the services it needs and nothing around them.
    /// </remarks>
    private static ServiceProvider Verbed()
    {
        var config = ServerConfigExtensions.Read(ServerConfigExtensions.Settings());

        return new ServiceCollection()
            .AddLogging(logging => logging.UseStderr())
            .AddSingleton(config)
            .AddCleanUp()
            .BuildServiceProvider();
    }
}
