namespace Snail.MCP.CleanUp.Domain;

/// <summary>One program running on this machine, as the process table shows it.</summary>
/// <param name="Pid">Its process id.</param>
/// <param name="ParentPid">The id of the process that started it; 1 once it has been handed to launchd.</param>
/// <param name="StartedAt">When it started, to the second.</param>
/// <param name="Command">Its command line.</param>
public sealed record RunningProcess(int Pid, int ParentPid, DateTimeOffset StartedAt, string Command)
{
    /// <summary>The key that names this process and no later one that is given the same id.</summary>
    public string Key => $"{Pid}@{StartedAt.ToUnixTimeSeconds()}";

    /// <summary>The name of the program, without its directory or its arguments.</summary>
    public string Program => Path.GetFileName(Command.Split(' ', 2)[0]);
}
