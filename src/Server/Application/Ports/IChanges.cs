namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>Word from the machine that something a census would see has changed, so that a census is not taken on a guess.</summary>
public interface IChanges
{
    /// <summary>Starts or keeps following the directories the last census listed and the processes of these agents.</summary>
    void Follow(IReadOnlyList<int> agents);

    /// <summary>Stops following anything; a server that does not take the timed census has nothing to be told.</summary>
    void Leave();

    /// <summary>What was said last, newest first: a directory that changed, or an agent that forked or ended.</summary>
    IReadOnlyList<string> Heard { get; }

    /// <summary>Whether something changed since the last call, forgetting it.</summary>
    bool TryTake();

    /// <summary>Waits until something changes or this long has passed, whichever is first; at once when a change is waiting.</summary>
    Task WaitAsync(TimeSpan most, CancellationToken cancellationToken);
}
