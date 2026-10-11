namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>The outside programs this server looks and acts through: ps, lsof, simctl, adb, docker.</summary>
public interface IPrograms
{
    /// <summary>How long a reading is shared: long enough to cover one census, short enough that the next one reads again.</summary>
    static TimeSpan Census { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Runs a program to the end, or until its time is up, and says how it ended.</summary>
    Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Runs a program that only reads, sharing its answer with everyone who asks the same within <paramref name="freshness"/>.</summary>
    /// <remarks>
    /// A census asks every kind and every trail at once, and several of them ask the same question — whether
    /// Docker runs, which ports are listened on, which simulators exist. One answer, read once and shared, is the
    /// same answer for all of them; a command that changes something, or checks right before it does, never comes here.
    /// </remarks>
    Task<ProgramRun> ReadAsync(string program, IReadOnlyList<string> arguments, TimeSpan freshness, TimeSpan timeout, CancellationToken cancellationToken);
}
