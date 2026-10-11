namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>The outside programs this server looks and acts through: ps, lsof, simctl, adb, docker.</summary>
public interface IPrograms
{
    /// <summary>Runs a program to the end, or until its time is up, and says how it ended.</summary>
    Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
