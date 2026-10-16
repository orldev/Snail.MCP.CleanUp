namespace Snail.MCP.CleanUp.Domain;

/// <summary>The sessions of this machine could not be told apart into going and ended, so no census can be taken.</summary>
/// <remarks>
/// An owner the census cannot see going is taken for ended, and what it holds is removed on ownership. A reading of the
/// machine that failed would hand a running session's things to the cleanup of another, so it stops the census instead.
/// </remarks>
public sealed class SessionsUnknownException : Exception
{
    /// <summary>The failure, with why the sessions are unknown.</summary>
    public SessionsUnknownException(string message)
        : base(message)
    {
    }

    /// <summary>The failure with a message of its own.</summary>
    public SessionsUnknownException()
        : base("the sessions of this machine could not be told")
    {
    }

    /// <summary>The failure, with the one that caused it.</summary>
    public SessionsUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
