namespace Snail.MCP.CleanUp.Adapters;

/// <summary>No word from the machine: every wait runs its full length and counts as a change, which makes the watch a plain timer.</summary>
/// <remarks>Where the kernel cannot be asked — off macOS — the census is taken every half a minute as it always was.</remarks>
public sealed class TimedChanges(TimeProvider clock) : IChanges
{
    /// <inheritdoc/>
    public void Follow(IReadOnlyList<int> agents)
    {
    }

    /// <inheritdoc/>
    public void Leave()
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Heard => [];

    /// <inheritdoc/>
    public bool Take() => true;

    /// <inheritdoc/>
    public Task WaitAsync(TimeSpan most, CancellationToken cancellationToken) => Task.Delay(most, clock, cancellationToken);
}
