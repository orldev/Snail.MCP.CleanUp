using Microsoft.Extensions.Configuration;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>Builds the settings the server runs on from the environment.</summary>
public static class ServerConfigExtensions
{
    /// <summary>Everything configured for this server.</summary>
    /// <remarks>
    /// Built apart from the host's own configuration on purpose: an <c>appsettings.json</c> lying in the
    /// working directory of whoever started the server is not a setting of this server and must not become
    /// one by accident.
    /// </remarks>
    public static IConfiguration Settings(System.Collections.IDictionary? variables = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(EnvironmentSettings.Read(variables ?? Environment.GetEnvironmentVariables()))
            .Build();

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
