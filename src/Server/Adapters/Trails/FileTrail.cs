using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The files and directories of the places leftovers show up in, a few levels deep.</summary>
/// <remarks>
/// Besides the places every Mac has, it looks wherever a rule points: a rule learned for a cache three
/// levels down is useless if the trail never goes three levels down. Links are listed but never followed,
/// so a link into a volume of millions of files costs one entry, not the census.
/// </remarks>
public sealed class FileTrail : ITrail
{
    /// <summary>How many entries one census reads at most; past it the trail says it stopped short.</summary>
    public const int MostEntries = 50_000;

    private readonly IReadOnlyList<(string Directory, int Depth)> _watched;

    /// <summary>A trail over the places of a Mac.</summary>
    public FileTrail()
        : this(MacPlaces.Watched)
    {
    }

    /// <summary>A trail over the given places.</summary>
    public FileTrail(IReadOnlyList<(string Directory, int Depth)> watched) => _watched = watched;

    /// <inheritdoc/>
    public string Name => "file";

    /// <inheritdoc/>
    public Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var places = _watched.Concat(Pointed(rules)).GroupBy(place => place.Directory, StringComparer.Ordinal).Select(group => (group.Key, group.Max(place => place.Depth)));
        var traces = new Dictionary<string, Trace>(StringComparer.Ordinal);

        foreach (var (directory, depth) in places)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Walk(directory, depth, traces);
        }

        return Task.FromResult<IReadOnlyList<Trace>>([.. traces.Values]);
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) =>
        Task.FromResult(trace with { Bytes = Sizes.Of(trace.Key) });

    private static IEnumerable<(string Directory, int Depth)> Pointed(IReadOnlyList<Rule> rules) =>
        rules
            .Where(rule => rule.Action != RuleAction.Ignore && string.Equals(rule.Trail, "file", StringComparison.Ordinal))
            .Select(rule => (Glob.Root(rule.Pattern), Glob.Depth(rule.Pattern)))
            .Where(place => place.Item2 is > 0 and <= 4)
            .Select(place => (place.Item1, place.Item2!.Value));

    private static void Walk(string directory, int depth, Dictionary<string, Trace> traces)
    {
        if (depth <= 0 || traces.Count >= MostEntries)
            return;

        foreach (var entry in Entries(directory))
        {
            if (traces.Count >= MostEntries)
                return;

            traces.TryAdd(entry.FullName, new Trace("file", entry.FullName, entry.FullName));

            if (entry is DirectoryInfo && entry.LinkTarget is null)
                Walk(entry.FullName, depth - 1, traces);
        }
    }

    private static IReadOnlyList<FileSystemInfo> Entries(string directory)
    {
        try
        {
            return [.. new DirectoryInfo(directory).EnumerateFileSystemInfos()];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }
}

/// <summary>The space a file or a directory takes, counted with a ceiling so that a huge tree cannot hold up a census.</summary>
public static class Sizes
{
    /// <summary>How many entries are counted before the count stops and says «at least».</summary>
    public const int MostCounted = 200_000;

    /// <summary>The bytes under a path; null when it cannot be read at all.</summary>
    public static long? Of(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;

            if (!Directory.Exists(path))
                return null;

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

            return new DirectoryInfo(path).EnumerateFiles("*", options).Take(MostCounted).Sum(file => file.Length);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
