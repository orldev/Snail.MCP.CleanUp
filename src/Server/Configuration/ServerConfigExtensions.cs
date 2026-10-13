using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Builds the settings the server runs on from its settings file and its variables.</summary>
public static class ServerConfigExtensions
{
    /// <summary>The server's short name, which names its settings file and its variables.</summary>
    public const string Server = "cleanup";

    /// <summary>Everything configured for this server: the first file of Snail.Sdk's settings chain, then the <c>SNAIL_MCP_CLEANUP_</c> variables, which win.</summary>
    /// <remarks>
    /// Built apart from the host's own configuration on purpose: an <c>appsettings.json</c> lying in the
    /// working directory of whoever started the server is not a setting of this server and must not become
    /// one by accident.
    /// </remarks>
    public static IConfiguration Settings() => new ConfigurationBuilder().AddServerSettings(Server).Build();

    /// <summary>The settings of the given file, when there is one, under the given variables.</summary>
    public static IConfiguration Settings(System.Collections.IDictionary variables, string? file = null) =>
        new ConfigurationBuilder().AddServerSettings(Server, file, variables).Build();

    /// <summary>What the settings say, checked before anything runs on them.</summary>
    public static ServerConfig Read(IConfiguration settings)
    {
        var config = new ServerConfig();

        settings.GetSection(ServerConfig.SectionName).Bind(config);
        settings.Bind(config);

        Validate(config);

        return config;
    }

    /// <summary>Refuses a setting that cannot work at start-up, so a typo stops the process instead of being repaired in silence.</summary>
    internal static void Validate(ServerConfig config)
    {
        var faults = new List<string>();

        if (!Path.IsPathFullyQualified(config.DataDirectory))
            faults.Add($"DataDirectory must be an absolute path, not '{config.DataDirectory}'");

        if (!Path.IsPathFullyQualified(config.TrashDirectory))
            faults.Add($"TrashDirectory must be an absolute path, not '{config.TrashDirectory}'");

        if (faults.Count > 0)
            throw new InvalidOperationException($"invalid configuration: {string.Join("; ", faults)}");
    }
}
