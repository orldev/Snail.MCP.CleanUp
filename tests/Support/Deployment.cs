using Microsoft.Extensions.Configuration;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>The environment a test starts the server in, written the way a client writes it.</summary>
internal static class Deployment
{
    public static Dictionary<string, string> Variables(Scratch scratch, params (string Name, string Value)[] own)
    {
        var variables = new Dictionary<string, string>
        {
            [$"{EnvironmentSettings.Prefix}DATA_DIRECTORY"] = scratch.Path
        };

        foreach (var (name, value) in own)
            variables[$"{EnvironmentSettings.Prefix}{name}"] = value;

        return variables;
    }

    public static IConfiguration Settings(Scratch scratch, params (string Name, string Value)[] own) =>
        ServerConfigExtensions.Settings(Variables(scratch, own));
}
