namespace Snail.MCP.CleanUp.Domain;

/// <summary>One program running on this machine, as the process table shows it.</summary>
/// <param name="Pid">Its process id.</param>
/// <param name="ParentPid">The id of the process that started it; 1 once it has been handed to launchd.</param>
/// <param name="StartedAt">When it started, to the second.</param>
/// <param name="Command">Its command line.</param>
/// <remarks>
/// The program is read apart from the command line because a command line does not quote its path:
/// <c>/Applications/Visual Studio Code.app/Contents/MacOS/Code</c> would otherwise be a program called
/// <c>Visual</c> with arguments.
/// </remarks>
public sealed record RunningProcess(int Pid, int ParentPid, DateTimeOffset StartedAt, string Command)
{
    /// <summary>The path of the executable, when the process table could say it.</summary>
    public string? Executable { get; init; }

    /// <summary>The name the program was started under, its first argument, when the process table could say it.</summary>
    /// <remarks>
    /// One executable can run under many names: Claude Code's single binary runs as <c>claude</c> and as the <c>ugrep</c>
    /// its shells call — measured on 2026-10-12, when the path alone named every grep of a session an agent. The name
    /// it was started under is what says which of them it is, and it is what ps prints too.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>The session of Claude Code its environment names (<c>CLAUDE_CODE_SESSION_ID</c>); null when it names none or could not be read.</summary>
    /// <remarks>
    /// Read only of the processes of the user this server runs as, and only where the kernel shows the environment: on macOS,
    /// where Apple's own programs hide theirs.
    /// </remarks>
    public string? SessionId { get; init; }

    /// <summary>The process of the agent its environment names (<c>CLAUDE_PID</c>); null when it names none or could not be read.</summary>
    public int? AgentPid { get; init; }

    /// <summary>The key that names this process and no later one that is given the same id.</summary>
    public string Key => $"{Pid}@{StartedAt.ToUnixTimeSeconds()}";

    /// <summary>The name of the program, without its directory or its arguments.</summary>
    public string Program => Name ?? Path.GetFileName(Executable ?? Command.Split(' ', 2)[0]);
}
