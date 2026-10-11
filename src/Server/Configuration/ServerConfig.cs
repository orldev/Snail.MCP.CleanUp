namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The settings this server runs on.</summary>
public sealed class ServerConfig
{
    /// <summary>The section a settings file may nest these under.</summary>
    public const string SectionName = "Snail";

    /// <summary>Where the ledger, the learned rules and the logs of tracked processes live.</summary>
    /// <remarks>
    /// One directory for every session on the machine, because the point of the ledger is that a session
    /// which died is cleaned up by the next one: a directory per session would die with it.
    /// </remarks>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".snail-mcp-cleanup");
}
