using System.Collections.Concurrent;
using System.IO.Enumeration;
using Snail.MCP.CleanUp.Adapters.Native;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The space a file or a directory takes, counted with a ceiling and remembered while the directory stays as it was.</summary>
/// <remarks>
/// The census every thirty seconds counted the same trees again each time: 0.7 GB of build products, caches and
/// scratchpads, 40 ms and 6 MB a census, measured on 2026-10-12. A directory's size is kept while its own write time
/// is unchanged, and for <see cref="Kept"/> at most — that time moves when an entry is added to or removed from the
/// directory itself, not when a file deep inside it grows, so the ceiling bounds how old a size can be. A size only
/// says what a cleanup would give back, and one a few minutes old says it honestly.
/// <para>
/// A count that stops at <see cref="MostCounted"/> says so: its size is «at least», never the size. It once stopped at
/// 200 000 files without a word, and a scratchpad of 50 GB — copies of repositories with their build trees and hang
/// dumps of 1–2 GB each — was reported as 3.76 GB (2026-10-13), because the dumps lay past the files it had counted.
/// On macOS the count is the space the blocks take, read a directory at a time (<see cref="BulkSizes"/>); elsewhere, and
/// where that cannot open a directory, it is the length of every file.
/// </para>
/// </remarks>
public sealed class Sizes(TimeProvider clock)
{
    /// <summary>How many entries are counted before the count stops and says «at least».</summary>
    /// <remarks>About 1.4 s of a census on an Apple Silicon Mac, once per <see cref="Kept"/> for a tree that large.</remarks>
    public const int MostCounted = 1_000_000;

    private readonly long _mostCounted = MostCounted;

    private readonly ConcurrentDictionary<string, Counted> _counted = new(StringComparer.Ordinal);

    private DateTimeOffset _swept = clock.GetUtcNow();

    /// <summary>How long a directory's size is believed while its own write time stays the same.</summary>
    public static TimeSpan Kept { get; } = TimeSpan.FromMinutes(10);

    /// <summary>Sizes that stop counting after <paramref name="mostCounted"/> entries rather than <see cref="MostCounted"/>.</summary>
    internal Sizes(TimeProvider clock, long mostCounted)
        : this(clock) => _mostCounted = mostCounted;

    /// <summary>The bytes under a path, and whether the count stopped short of them; null when it cannot be read at all.</summary>
    public Measured? Of(string path)
    {
        try
        {
            if (File.Exists(path))
                return new Measured(new FileInfo(path).Length, IsAtLeast: false);

            if (!Directory.Exists(path))
                return Forgotten(path);

            var now = clock.GetUtcNow();
            var written = Directory.GetLastWriteTimeUtc(path);

            Sweep(now);

            if (_counted.TryGetValue(path, out var known) && known.Written == written && now - known.At < Kept)
                return known.Size;

            var size = Total(path);

            _counted[path] = new Counted(written, now, size);

            return size;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return Forgotten(path);
        }
    }

    /// <summary>The bytes of the files under a directory: the kernel's count on macOS, the lengths in the listing elsewhere.</summary>
    private Measured Total(string directory)
    {
        if (OperatingSystem.IsMacOS() && BulkSizes.Of(directory, _mostCounted) is { } counted)
            return new Measured(counted.Bytes, !counted.IsComplete);

        return Listed(directory);
    }

    /// <summary>The lengths of the files under a directory, read from its listing without a <see cref="FileInfo"/> per file.</summary>
    private Measured Listed(string directory)
    {
        var entries = 0L;
        var bytes = 0L;
        var lengths = new FileSystemEnumerable<long>(directory, (ref entry) => entry.IsDirectory ? 0 : entry.Length, Everything);

        foreach (var length in lengths)
        {
            if (++entries > _mostCounted)
                return new Measured(bytes, IsAtLeast: true);

            bytes += length;
        }

        return new Measured(bytes, IsAtLeast: false);
    }

    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    private Measured? Forgotten(string path)
    {
        _counted.TryRemove(path, out _);

        return null;
    }

    /// <summary>Drops the sizes of paths nobody asked about for a while, so that the gone ones do not pile up.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - _swept < Kept)
            return;

        _swept = now;

        foreach (var (path, counted) in _counted)
        {
            if (now - counted.At >= Kept)
                _counted.TryRemove(path, out _);
        }
    }

    private sealed record Counted(DateTime Written, DateTimeOffset At, Measured Size);
}
