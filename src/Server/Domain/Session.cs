namespace Snail.MCP.CleanUp.Domain;

/// <summary>A session of an agent on this machine: an owner the account knows of, with the process of its agent.</summary>
/// <param name="Owner">The session, as <see cref="AgentOwners"/> names it.</param>
/// <param name="Pid">The process of the agent, when it is known.</param>
/// <param name="IsAlive">Whether its agent's process is still the program that started it.</param>
/// <remarks>
/// Snail.Toolkit.Cleanup knows an owner as a <see cref="Holder"/>: who it is, whether it is going, when it began and the
/// directory it works in. The agent's process is this server's alone, so it rides on a holder of the server's own, and
/// the kinds that follow an agent's children read it back from the holders a census hands them.
/// </remarks>
public sealed record Session(Owner Owner, int? Pid, bool IsAlive) : Holder(Owner, IsAlive)
{
    /// <summary>The id the agent itself gives the session.</summary>
    public string Id => Owner.Id;
}
