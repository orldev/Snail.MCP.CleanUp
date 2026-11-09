using System.Reflection;
using System.Text.RegularExpressions;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Contracts;

/// <summary>The registry manifest and the README name the settings the server reads, all of them and no others.</summary>
public sealed partial class ManifestContractTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (!File.Exists(Path.Combine(directory.FullName, "Snail.MCP.CleanUp.slnx")))
                directory = directory.Parent!;

            return directory.FullName;
        }
    }

    private static IEnumerable<string> Settings =>
        typeof(ServerConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => $"{EnvironmentSettings.Prefix}{Snake().Replace(property.Name, "$1_$2").ToUpperInvariant()}")
            .Order(StringComparer.Ordinal);

    [Fact]
    public void Manifest_NamesEverySettingTheServerReads()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, ".mcp", "server.json")))!;
        var named = manifest["packages"]![0]!["environmentVariables"]!.AsArray().Select(variable => variable!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal);

        Assert.Equal(Settings, named);
    }

    /// <summary>A default copied from the manifest into a client's registration starts the server, <c>~</c> and all.</summary>
    [Fact]
    public void Manifest_DefaultsAsWritten_StartTheServer()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, ".mcp", "server.json")))!;
        var defaults = manifest["packages"]![0]!["environmentVariables"]!.AsArray()
            .Where(variable => variable!["default"] is not null)
            .ToDictionary(variable => variable!["name"]!.GetValue<string>(), variable => variable!["default"]!.GetValue<string>());

        var config = ServerConfigExtensions.Read(ServerConfigExtensions.Settings(defaults));

        Assert.Equal(Path.Combine(Snail.Sdk.Mcp.Hosting.ServerFolders.Home, ".s3l-mcp-cleanup"), config.DataDirectory);
        Assert.Equal(Path.Combine(Snail.Sdk.Mcp.Hosting.ServerFolders.Home, ".claude", "settings.json"), config.ClaudeSettings);
    }

    [Fact]
    public void Readme_NamesEverySettingTheServerReads()
    {
        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));

        Assert.All(Settings, setting => Assert.Contains($"`{setting[EnvironmentSettings.Prefix.Length..]}`", readme, StringComparison.Ordinal));
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex Snake();
}
