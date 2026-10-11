namespace Snail.MCP.CleanUp.Domain;

/// <summary>How running an outside program ended.</summary>
/// <param name="ExitCode">Its exit code; -1 when it never ran or was stopped.</param>
/// <param name="Output">What it wrote to stdout.</param>
/// <param name="Error">What it wrote to stderr.</param>
/// <param name="IsMissing">Whether the program is not installed here.</param>
/// <param name="IsTimedOut">Whether it ran out of time and was stopped.</param>
public sealed record ProgramRun(int ExitCode, string Output, string Error, bool IsMissing = false, bool IsTimedOut = false)
{
    /// <summary>Whether it ran and said it succeeded.</summary>
    public bool IsSuccess => ExitCode == 0 && !IsMissing && !IsTimedOut;

    /// <summary>A program that is not installed.</summary>
    public static ProgramRun Missing(string program) => new(-1, string.Empty, $"{program} is not installed here", IsMissing: true);

    /// <summary>Why it failed, in one line, for a note or a release.</summary>
    public string Failure => IsTimedOut ? "it ran out of time" : Error.Trim() is { Length: > 0 } said ? said.Split('\n')[0] : $"exit code {ExitCode}";
}
