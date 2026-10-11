namespace Snail.MCP.CleanUp.Adapters.Native;

/// <summary>The process table read from the kernel, kept for a couple of seconds so one census reads it once.</summary>
public sealed class NativeProcessTable(TimeProvider clock) : IProcessTable, IDisposable
{
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _reading = new(1, 1);

    private (DateTimeOffset At, IReadOnlyList<RunningProcess> Processes)? _last;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken);

        try
        {
            if (_last is { } last && clock.GetUtcNow() - last.At < Freshness)
                return last.Processes;

            var processes = LibProc.List();

            _last = (clock.GetUtcNow(), processes);

            return processes;
        }
        finally
        {
            _reading.Release();
        }
    }

    /// <inheritdoc/>
    public void Refresh() => _last = null;

    /// <inheritdoc/>
    public void Dispose() => _reading.Dispose();
}
