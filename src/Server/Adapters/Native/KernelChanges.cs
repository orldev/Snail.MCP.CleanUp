using System.Diagnostics;
using System.Runtime.InteropServices;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Native;

/// <summary>The macOS kernel's word that a watched directory gained or lost an entry, or that an agent started a command or ended.</summary>
/// <remarks>
/// <para>
/// kqueue, not FSEvents. A directory is told about only when an entry is added to it, removed or renamed — exactly
/// what a census would see differently — and nothing under it reaches this process. An FSEvents stream, which
/// FileSystemWatcher uses, watches a whole tree, and one over the home directory would hand this process every file
/// written anywhere under it.
/// </para>
/// <para>
/// An agent is followed by its own process: it forks for every command it runs and ends when its session does. A
/// program a command leaves running is a child of that command's shell, so the census after the fork sees it while
/// it is still a child, which is what proves it the agent's.
/// </para>
/// </remarks>
public sealed partial class KernelChanges : IChanges, IDisposable
{
    /// <summary>How many directories are followed at most; past it the timer finds the rest.</summary>
    public const int MostDirectories = 1_000;

    private const short VnodeFilter = -4;

    private const short ProcessFilter = -5;

    private const ushort Add = 0x0001;

    private const ushort Delete = 0x0002;

    private const ushort Clear = 0x0020;

    private const uint Removed = 0x00000001;

    private const uint Written = 0x00000002;

    private const uint Renamed = 0x00000020;

    private const uint Exited = 0x80000000;

    private const uint Forked = 0x40000000;

    private const int EventsOnly = 0x00008000;

    private readonly FileTrail _trail;

    private readonly int _queue;

    private readonly Lock _following = new();

    private readonly Dictionary<string, int> _directories = new(StringComparer.Ordinal);

    private readonly HashSet<int> _agents = [];

    private readonly SemaphoreSlim _woken = new(0, 1);

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _heard = new();

    private int _isChanged;

    private volatile bool _isDisposed;

    /// <summary>A kernel queue, read on a thread of its own for as long as this lives.</summary>
    public KernelChanges(FileTrail trail)
    {
        _trail = trail;
        _queue = KernelQueue();

        if (_queue < 0)
            throw new InvalidOperationException($"kqueue failed: {Marshal.GetLastPInvokeError()}");

        new Thread(Read) { IsBackground = true, Name = "kernel changes" }.Start();
    }

    /// <inheritdoc/>
    public void Follow(IReadOnlyList<int> agents) => Follow(_trail.Listed, agents);

    /// <inheritdoc/>
    public void Leave() => Follow([], []);

    private void Follow(IReadOnlyList<string> directories, IReadOnlyList<int> agents)
    {
        lock (_following)
        {
            var wanted = directories.Where(directory => !MacPlaces.Restless.Contains(directory)).Take(MostDirectories).ToHashSet(StringComparer.Ordinal);

            foreach (var (directory, descriptor) in _directories.Where(pair => !wanted.Contains(pair.Key)).ToList())
            {
                _ = Close(descriptor);
                _directories.Remove(directory);
            }

            foreach (var directory in wanted.Where(directory => !_directories.ContainsKey(directory)))
            {
                var descriptor = Open(directory, EventsOnly);

                if (descriptor < 0)
                    continue;

                if (TryRegister(new Event((nuint)descriptor, VnodeFilter, Add | Clear, Written | Removed | Renamed)))
                    _directories[directory] = descriptor;
                else
                    _ = Close(descriptor);
            }

            foreach (var gone in _agents.Where(agent => !agents.Contains(agent)).ToList())
            {
                TryRegister(new Event((nuint)gone, ProcessFilter, Delete, 0));
                _agents.Remove(gone);
            }

            foreach (var agent in agents.Where(agent => agent > 0 && !_agents.Contains(agent)))
            {
                if (TryRegister(new Event((nuint)agent, ProcessFilter, Add | Clear, Forked | Exited)))
                    _agents.Add(agent);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryTake() => Interlocked.Exchange(ref _isChanged, 0) == 1;

    /// <inheritdoc/>
    /// <remarks>
    /// A wake-up can outlive its word: the reading thread wakes the waiter on every word, and a wait that ended at once on
    /// a change already marked never spent that wake-up, so it is still there after the caller took the change with
    /// <see cref="TryTake"/>. Such a wake-up is spent and the wait goes on for what is left of its time: only a change
    /// marked now ends it early.
    /// </remarks>
    public async Task WaitAsync(TimeSpan most, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        while (Volatile.Read(ref _isChanged) == 0)
        {
            var left = most - Stopwatch.GetElapsedTime(started);

            if (left <= TimeSpan.Zero || !await _woken.WaitAsync(left, cancellationToken))
                return;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Heard => [.. _heard.Reverse()];

    /// <summary>The directories and agents followed now, for what the diagnosis reports.</summary>
    public (int Directories, int Agents) Followed
    {
        get
        {
            lock (_following)
                return (_directories.Count, _agents.Count);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Once only: a second call would close the queue's number again, and by then the number may be another file's — the
    /// ledger's, a socket's — which would be closed under its owner.
    /// </remarks>
    public void Dispose()
    {
        lock (_following)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            foreach (var descriptor in _directories.Values)
                _ = Close(descriptor);

            _directories.Clear();
            _agents.Clear();
        }

        _ = Close(_queue);
        _woken.Dispose();
    }

    private void Read()
    {
        var received = new Event[32];
        var second = new TimeSpec(1, 0);

        while (!_isDisposed)
        {
            var count = Wait(_queue, null, 0, received, received.Length, ref second);

            if (count < 0 && _isDisposed)
                return;

            if (count <= 0)
                continue;

            if (!IsHeard(received, count))
                continue;

            if (_woken.CurrentCount == 0)
            {
                try
                {
                    _woken.Release();
                }
                catch (Exception missed) when (missed is SemaphoreFullException or ObjectDisposedException)
                {
                }
            }
        }
    }

    /// <summary>Writes down what the kernel said and marks a change, when any of it is about something still followed.</summary>
    /// <remarks>
    /// The kernel may hand word over a moment before <see cref="Leave"/> and this thread mark it a moment after, when the
    /// caller has already taken stock with <see cref="TryTake"/>: a server that left would be told of a change all the same.
    /// So word is weighed under the lock that changes what is followed — it is either marked before the leaving, where the
    /// caller's next <see cref="TryTake"/> forgets it, or found to be about what is no longer followed, and dropped.
    /// </remarks>
    private bool IsHeard(Event[] received, int count)
    {
        var isChanged = false;

        lock (_following)
        {
            for (var index = 0; index < count; index++)
            {
                if (Said(received[index]) is not { } said)
                    continue;

                Remember(said);
                isChanged = true;
            }

            if (isChanged)
                Interlocked.Exchange(ref _isChanged, 1);
        }

        return isChanged;
    }

    /// <summary>What a word of the kernel is about, or nothing when that is no longer followed.</summary>
    private string? Said(Event heard)
    {
        if (heard.Filter == ProcessFilter)
            return _agents.Contains((int)heard.Identity) ? $"agent {heard.Identity}" : null;

        foreach (var (directory, descriptor) in _directories)
        {
            if ((nuint)descriptor == heard.Identity)
                return directory;
        }

        return null;
    }

    private void Remember(string said)
    {
        _heard.Enqueue(said);

        while (_heard.Count > 20)
            _heard.TryDequeue(out _);
    }

    private bool TryRegister(Event change)
    {
        var none = new TimeSpec(0, 0);

        return Wait(_queue, [change], 1, null, 0, ref none) >= 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Event(nuint Identity, short Filter, ushort Flags, uint FilterFlags, nint Data = 0, nint UserData = 0);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TimeSpec(long Seconds, long Nanoseconds);

    [LibraryImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static partial int KernelQueue();

    [LibraryImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static partial int Wait(int queue, Event[]? changes, int changeCount, [Out] Event[]? events, int eventCount, ref TimeSpec timeout);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int descriptor);
}
