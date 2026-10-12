using System.IO.Enumeration;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The files and directories of the places leftovers show up in, a few levels deep.</summary>
/// <remarks>
/// Besides the places every Mac has, it looks wherever a rule points: a rule learned for a cache three
/// levels down is useless if the trail never goes three levels down. Links are listed but never followed,
/// so a link into a volume of millions of files costs one entry, not the census.
/// </remarks>
/// <param name="watched">The places the trail walks, each with how many levels down it looks.</param>
/// <param name="sizes">How the size of what it finds is counted.</param>
public sealed class FileTrail(IReadOnlyList<(string Directory, int Depth)> watched, Sizes sizes) : ITrail
{
    private readonly Sizes _sizes = sizes;

    /// <summary>How many entries one census reads at most; past it the trail says it stopped short.</summary>
    public const int MostEntries = 50_000;

    private readonly IReadOnlyList<(string Directory, int Depth)> _watched = watched;

    /// <summary>A trail over the places of a Mac.</summary>
    public FileTrail(Sizes sizes)
        : this(MacPlaces.Watched, sizes)
    {
    }

    /// <inheritdoc/>
    public string Name => "file";

    /// <summary>The directories the last walk listed: where a new entry is something a census would see.</summary>
    public IReadOnlyList<string> Listed { get; private set; } = [];

    /// <inheritdoc/>
    public Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var places = _watched.Concat(Pointed(rules)).GroupBy(place => place.Directory, StringComparer.Ordinal).Select(group => (group.Key, group.Max(place => place.Depth)));
        var traces = new Dictionary<string, Trace>(StringComparer.Ordinal);
        var listed = new List<string>();

        foreach (var (directory, depth) in places)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Walk(directory, depth, traces, listed);
        }

        Listed = listed;

        return Task.FromResult<IReadOnlyList<Trace>>([.. traces.Values]);
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) =>
        Task.FromResult(trace with { Bytes = _sizes.Of(trace.Key) });

    private static IEnumerable<(string Directory, int Depth)> Pointed(IReadOnlyList<Rule> rules) =>
        rules
            .Where(rule => rule.Action != RuleAction.Ignore && string.Equals(rule.Trail, "file", StringComparison.Ordinal))
            .Select(rule => (Glob.Root(rule.Pattern), Glob.Depth(rule.Pattern)))
            .Where(place => place.Item2 is > 0 and <= 4)
            .Select(place => (place.Item1, place.Item2!.Value));

    private static void Walk(string directory, int depth, Dictionary<string, Trace> traces, List<string> listed)
    {
        if (depth <= 0 || traces.Count >= MostEntries)
            return;

        listed.Add(directory);

        foreach (var (path, isFollowed) in Entries(directory))
        {
            if (traces.Count >= MostEntries)
                return;

            traces.TryAdd(path, new Trace("file", path, path));

            if (isFollowed)
                Walk(path, depth - 1, traces, listed);
        }
    }

    /// <summary>The entries of a directory and whether each is a directory to go into: a real one, not a link to one.</summary>
    /// <remarks>Read from the directory listing itself, without a <see cref="FileSystemInfo"/> or a look at a link's target per entry.</remarks>
    private static IReadOnlyList<(string Path, bool IsFollowed)> Entries(string directory)
    {
        try
        {
            return
            [
                .. new FileSystemEnumerable<(string, bool)>(
                    directory,
                    (ref entry) => (entry.ToFullPath(), entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0),
                    Listing)
            ];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };
}
