using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Asks the thread pool, from a thread of its own, how long it takes to start a piece of work, and logs it when the answer is slow.</summary>
/// <remarks>
/// A tool call that never answers leaves two suspects: the call never reached the server's code, or the pool had no
/// thread to run what came after it — the timer that answers a quick tool as overdue among it. A census walked the disk
/// on pool threads without awaiting, and on 2026-11-09 a cleanup_plan went 31 minutes without the overdue answer the
/// server gives at two. Asked from a thread of its own, the pool's delay is measured even when the pool itself is what
/// stopped, and the line, with the threads and the work waiting, says which of the two it was.
/// <para>
/// Since Snail.Toolkit.Cleanup 0.1.1 the census looks off the pool. The watch stays, to say so if the pool is ever held
/// again: a slow line after that is a new cause, not the census.
/// </para>
/// </remarks>
public sealed class PoolWatch(ILogger<PoolWatch> logger) : IHostedService, IDisposable
{
    /// <summary>How often the pool is asked.</summary>
    public static TimeSpan Every { get; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a piece of work may wait for a thread before the wait is logged.</summary>
    public static TimeSpan Slow { get; } = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopping = new();

    private Thread? _thread;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "pool watch" };
        _thread.Start();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => _stopping.CancelAsync();

    /// <inheritdoc/>
    public void Dispose() => _stopping.Dispose();

    /// <summary>How long the pool took to start a piece of work; null when it had not started it within <paramref name="patience"/>.</summary>
    /// <remarks>The signal is left to the collector when the work has not started: disposed, it would throw on the pool thread that runs it later.</remarks>
    public static TimeSpan? Probe(TimeSpan patience)
    {
        var started = new ManualResetEventSlim();
        var asked = Stopwatch.GetTimestamp();

        ThreadPool.UnsafeQueueUserWorkItem(static signal => signal.Set(), started, preferLocal: false);

        if (!started.Wait(patience))
            return null;

        started.Dispose();

        return Stopwatch.GetElapsedTime(asked);
    }

    /// <remarks>The watch ends when the host stops, and quietly when its token was disposed under it.</remarks>
    private void Run()
    {
        try
        {
            Watch(_stopping.Token);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
    }

    private void Watch(CancellationToken stopping)
    {
        while (!stopping.WaitHandle.WaitOne(Every))
        {
            var waited = Probe(Every);

            if (waited is null || waited >= Slow)
                Log.PoolSlow(logger, waited ?? Every, waited is null, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
        }
    }
}
