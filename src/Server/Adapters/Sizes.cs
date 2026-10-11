using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The space a file or a directory takes, counted with a ceiling and remembered while the directory stays as it was.</summary>
/// <remarks>
/// The census every thirty seconds counted the same trees again each time: 0.7 GB of build products, caches and
/// scratchpads, 40 ms and 6 MB a census, measured on 2026-10-12. A directory's size is kept while its own write time
/// is unchanged, and for <see cref="Kept"/> at most — that time moves when an entry is added to or removed from the
/// directory itself, not when a file deep inside it grows, so the ceiling bounds how old a size can be. A size only
/// says what a cleanup would give back, and one a few minutes old says it honestly.
/// </remarks>
public sealed class Sizes(TimeProvider clock)
{
    /// <summary>How many entries are counted before the count stops and says «at least».</summary>
    public const int MostCounted = 200_000;

    private readonly ConcurrentDictionary<string, Counted> _counted = new(StringComparer.Ordinal);

    private DateTimeOffset _swept = clock.GetUtcNow();

    /// <summary>How long a directory's size is believed while its own write time stays the same.</summary>
    public static TimeSpan Kept { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The bytes under a path; null when it cannot be read at all.</summary>
    public long? Of(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;

            if (!Directory.Exists(path))
                return Forgotten(path);

            var now = clock.GetUtcNow();
            var written = Directory.GetLastWriteTimeUtc(path);

            Sweep(now);

            if (_counted.TryGetValue(path, out var known) && known.Written == written && now - known.At < Kept)
                return known.Bytes;

            var bytes = Total(path);

            _counted[path] = new Counted(written, now, bytes);

            return bytes;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return Forgotten(path);
        }
    }

    /// <summary>The bytes of the files under a directory, read from its listing without a <see cref="FileInfo"/> per file.</summary>
    private static long Total(string directory)
    {
        var lengths = new FileSystemEnumerable<long>(directory, (ref entry) => entry.Length, Everything)
        {
            ShouldIncludePredicate = (ref entry) => !entry.IsDirectory
        };

        return lengths.Take(MostCounted).Sum();
    }

    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    private long? Forgotten(string path)
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

    private sealed record Counted(DateTime Written, DateTimeOffset At, long Bytes);
}
