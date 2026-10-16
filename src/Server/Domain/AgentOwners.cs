namespace Snail.MCP.CleanUp.Domain;

/// <summary>The owners this server writes resources under: the sessions of agents.</summary>
/// <remarks>
/// The kind is written into the ledger as <c>session:&lt;id&gt;</c>, into the names and labels of what a session made and
/// into the plans; changed, it would leave every row written before as nobody's session.
/// </remarks>
public static class AgentOwners
{
    /// <summary>The kind every session of an agent is written under.</summary>
    public const string SessionKind = "session";

    extension(Owner)
    {
        /// <summary>The session of an agent with this id.</summary>
        public static Owner Session(string id) => new(SessionKind, id);
    }
}
