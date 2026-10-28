using System.IO.Enumeration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>The <c>TestResults</c> folders of the repositories agents and the developer work in: dumps, coverage and logs of test runs.</summary>
/// <remarks>
/// A test run that hangs or crashes leaves a dump the size of the process in <c>TestResults</c> beside the test project,
/// and nothing ever removes it: <c>Snail.Toolkit.Web</c> held a hang dump of 6.2 GB from 2026-09-15 on 2026-10-13. What
/// is there only takes space — the next test run makes its own — so each folder is a cache of its repository, removed
/// when caches are asked for, and never while a test may be writing into it: not when anything in it was written in the
/// last <see cref="InUse"/>, judged again right before it goes.
/// <para>
/// The repositories are the ones the machine already names, never the whole disk: the directories sessions work in, and
/// the folders VS Code keeps state for — each only if it holds <c>.git</c>. They are searched a few levels down, past
/// hidden folders and build trees, at most every <see cref="Searched"/>.
/// </para>
/// </remarks>
public sealed class TestResultsKind(Repositories repositories, Disposal disposal, Sizes sizes, TimeProvider clock) : IResourceKind
{
    /// <summary>The name of the folder test runs write into.</summary>
    public const string Folder = "TestResults";

    /// <summary>How many levels below a repository a <see cref="Folder"/> is looked for: <c>src/Tests/Some.Tests/TestResults</c>.</summary>
    public const int Deepest = 4;

    /// <summary>How many entries of a folder are read to tell when it was last written; past them it is taken to be in use.</summary>
    public const int MostRead = 10_000;

    private static readonly HashSet<string> Passed = new(["bin", "obj", "node_modules", "packages", "artifacts"], StringComparer.Ordinal);

    private readonly Lock _searching = new();

    private Search? _last;

    /// <summary>How recently written a folder counts as one a test run may still be writing into.</summary>
    public static TimeSpan InUse { get; } = TimeSpan.FromHours(1);

    /// <summary>How long the folders found in the repositories are taken as read: test runs make them rarely, not every census.</summary>
    public static TimeSpan Searched { get; } = TimeSpan.FromMinutes(10);

    /// <inheritdoc/>
    public string Name => MachineKinds.TestResults;

    /// <inheritdoc/>
    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        var found = new List<Resource>();

        foreach (var (repository, path) in Found(holders))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Described(repository, path) is { } resource)
                found.Add(resource);
        }

        return Task.FromResult(new Observation(Name, found, []));
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(resource.Key))
            return Task.FromResult(Release.AlreadyGone(resource));

        if (Busy(resource.Key) is { } busy)
            return Task.FromResult(Release.NotTheSame(resource, busy));

        return Task.FromResult(disposal.Dispose(resource, resource.Key));
    }

    private Resource? Described(string repository, string path)
    {
        var size = sizes.Of(path);

        if (size is null || (size.Value.Bytes == 0 && !size.Value.IsAtLeast))
            return null;

        return new Resource
        {
            Kind = Name,
            Key = path,
            Title = $"test results of {Path.GetFileName(repository)} in {Path.GetRelativePath(repository, path)}: dumps, coverage and logs of test runs; the next test run makes them again",
            IsShared = true,
            IsRegenerable = true,
            Bytes = size.Value.Bytes,
            IsAtLeast = size.Value.IsAtLeast,
            Blocker = Busy(path)
        };
    }

    /// <summary>Why a test run may still be writing into the folder; null when nothing in it was written lately.</summary>
    private string? Busy(string path)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

        try
        {
            var newest = Directory.GetLastWriteTimeUtc(path);
            var read = 0;

            foreach (var written in new FileSystemEnumerable<DateTime>(path, (ref entry) => entry.LastWriteTimeUtc.UtcDateTime, options))
            {
                if (++read > MostRead)
                    return $"more than {MostRead} entries: when it was last written cannot be told cheaply, so a test run may still be writing into it";

                if (written > newest)
                    newest = written;
            }

            var since = clock.GetUtcNow().UtcDateTime - newest;

            return since < InUse ? $"written {Math.Max(since.TotalMinutes, 0):0} minutes ago: a test run may still be writing into it" : null;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return $"could not be read: {refused.Message}";
        }
    }

    /// <summary>The folders found in the repositories, searched again when they are older than <see cref="Searched"/> or the repositories changed.</summary>
    private IReadOnlyList<(string Repository, string Path)> Found(IReadOnlyList<Holder> sessions)
    {
        var worked = Repositories.Worked(sessions);
        var now = clock.GetUtcNow();

        lock (_searching)
        {
            if (_last is { } last && now - last.At < Searched && last.Worked.SequenceEqual(worked, StringComparer.Ordinal))
                return last.Found;
        }

        var found = repositories.Named(worked).SelectMany(repository => Within(repository).Select(path => (repository, path))).ToList();

        lock (_searching)
            _last = new Search(now, worked, found);

        return found;
    }

    /// <summary>The <see cref="Folder"/> folders of one repository, past hidden folders, build trees and links.</summary>
    private static IReadOnlyList<string> Within(string repository)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = Deepest - 1,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            return
            [
                .. new FileSystemEnumerable<string>(repository, (ref entry) => entry.ToFullPath(), options)
                {
                    ShouldIncludePredicate = static (ref entry) => entry.IsDirectory && entry.FileName.SequenceEqual(Folder),
                    ShouldRecursePredicate = static (ref entry) => !entry.FileName.StartsWith('.')
                        && !entry.FileName.SequenceEqual(Folder)
                        && !Passed.GetAlternateLookup<ReadOnlySpan<char>>().Contains(entry.FileName)
                }
            ];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>One search of the repositories: when, over which session directories, and what it found.</summary>
    private sealed record Search(DateTimeOffset At, IReadOnlyList<string> Worked, IReadOnlyList<(string Repository, string Path)> Found);
}
