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

        ServerSettings.Bind(Server, settings, source =>
        {
            source.GetSection(ServerConfig.SectionName).Bind(config);
            source.Bind(config);
        });

        Validate(config);

        return config;
    }

    /// <summary>Refuses a setting that cannot work at start-up, so a typo stops the process instead of being repaired in silence.</summary>
    /// <remarks>
    /// Each refused value is named as the operator set it, its variable and its key in the settings file
    /// (<see cref="ServerSettings.NameOfSetting"/>), and every one of them is said in the same line: a
    /// <see cref="StartRefusedException"/> is what <c>Program.cs</c> writes to stderr before it exits with 1.
    /// </remarks>
    /// <exception cref="StartRefusedException">A setting holds a value the server cannot run on.</exception>
    internal static void Validate(ServerConfig config)
    {
        var faults = new List<string>();

        if (!Path.IsPathFullyQualified(config.DataDirectory))
            faults.Add(Fault("DataDirectory", config.DataDirectory, "an absolute path"));

        if (!Path.IsPathFullyQualified(config.TrashDirectory))
            faults.Add(Fault("TrashDirectory", config.TrashDirectory, "an absolute path"));

        if (!(config.ScratchpadWarningGigabytes > 0))
            faults.Add(Fault("ScratchpadWarningGigabytes", config.ScratchpadWarningGigabytes, "a number of gigabytes above zero"));

        if (!(config.DiskFallWarningGigabytes > 0))
            faults.Add(Fault("DiskFallWarningGigabytes", config.DiskFallWarningGigabytes, "a number of gigabytes above zero"));

        if (!(config.SweepMinutes >= 0))
            faults.Add(Fault("SweepMinutes", config.SweepMinutes, "a number of minutes, 0 for never"));

        if (!(config.DockerCacheSweepGigabytes > 0))
            faults.Add(Fault("DockerCacheSweepGigabytes", config.DockerCacheSweepGigabytes, "a number of gigabytes above zero"));

        if (!(config.ScratchpadEraseHours > 0))
            faults.Add(Fault("ScratchpadEraseHours", config.ScratchpadEraseHours, "a number of hours above zero"));

        if (!(config.HistoryDays > 0))
            faults.Add(Fault("HistoryDays", config.HistoryDays, "a number of days above zero"));

        if (faults.Count > 0)
            throw new StartRefusedException(string.Join(" ", faults));
    }

    /// <summary>One refused value as a sentence: <c>SNAIL_MCP_CLEANUP_HISTORY_DAYS (or HistoryDays in the settings file) is '0'; it is a number of days above zero.</c></summary>
    private static string Fault(string key, object value, string expected) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{ServerSettings.NameOfSetting(Server, key)} is '{value}'; it is {expected}.");
}
