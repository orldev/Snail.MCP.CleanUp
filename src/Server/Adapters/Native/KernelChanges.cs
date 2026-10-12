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
    public async Task WaitAsync(TimeSpan most, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isChanged) == 1)
            return;

        await _woken.WaitAsync(most, cancellationToken);
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
    public void Dispose()
    {
        _isDisposed = true;

        lock (_following)
        {
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

            for (var index = 0; index < count; index++)
                Remember(received[index]);

            Interlocked.Exchange(ref _isChanged, 1);

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

    private void Remember(Event heard)
    {
        string said;

        if (heard.Filter == ProcessFilter)
        {
            said = $"agent {heard.Identity}";
        }
        else
        {
            lock (_following)
                said = _directories.FirstOrDefault(pair => (nuint)pair.Value == heard.Identity).Key ?? $"descriptor {heard.Identity}";
        }

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
