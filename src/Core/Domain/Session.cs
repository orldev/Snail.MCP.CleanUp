namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>One session of an agent on this machine, and whether it is still running.</summary>
/// <param name="Id">The id the agent itself gives the session.</param>
/// <param name="Pid">The process of the agent, when it is known.</param>
/// <param name="StartedAt">When the session began.</param>
/// <param name="Directory">The directory the agent works in.</param>
/// <param name="IsAlive">Whether its process is still the same program that started it.</param>
public sealed record Session(string Id, int? Pid, DateTimeOffset? StartedAt, string? Directory, bool IsAlive)
{
    /// <summary>The owner the session's resources are written under.</summary>
    public Owner Owner => Owner.Session(Id);
}
