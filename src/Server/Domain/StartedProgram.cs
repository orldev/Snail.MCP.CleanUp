namespace Snail.MCP.CleanUp.Domain;

/// <summary>A program started to outlive the call that started it.</summary>
/// <param name="Pid">Its process id.</param>
/// <param name="StartedAt">When it started; null when it ended before that could be read, as a command that fails at once does.</param>
public sealed record StartedProgram(int Pid, DateTimeOffset? StartedAt);
