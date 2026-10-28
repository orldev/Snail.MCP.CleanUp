using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>What the next build, restore or test run makes again inside a scratchpad: the outputs a running session may give back.</summary>
/// <remarks>
/// A running session's scratchpad is never released — the session writes into it — and on 2026-10-13 one grew to 50 GB
/// while the inventory said only that it was blocked: copies of repositories with their build trees, test results and
/// benchmark artifacts. Those are the session's own and cost a build to make again, so they are offered to the session
/// itself: <c>bin</c> and <c>obj</c> beside a <c>*.csproj</c>, <c>TestResults</c>, <c>BenchmarkDotNet.Artifacts</c>, and
/// <c>node_modules</c> beside a <c>package.json</c>. A Python environment (a folder with <c>pyvenv.cfg</c>) is no cheap
/// rebuild and is never looked into, nor a folder of installed tools (one holding <c>.store</c>), nor hidden folders.
/// <para>
/// One written in the last <see cref="Quiet"/> may be in a build's hands, and so may one a running program was started
/// from: either is left. The search and that judgement are kept for <see cref="Searched"/>, since a scratchpad's trees
/// are large and change by builds, not by censuses; the judgement is made again right before anything goes.
/// </para>
/// </remarks>
/// <param name="clock">What now is.</param>
/// <param name="quiet">How long an output must have gone unwritten before it is offered; <see cref="Quiet"/> when null.</param>
public sealed class BuildOutputs(TimeProvider clock, TimeSpan? quiet = null)
{
    /// <summary>How many levels below a scratchpad outputs are looked for: <c>repo/src/Server/bin</c> is four.</summary>
    public const int Deepest = 6;

    /// <summary>How many folders of one scratchpad are looked into at most.</summary>
    public const int MostDirectories = 20_000;

    /// <summary>How many entries of an output are read to tell when it was last written; past them it is taken to be in use.</summary>
    public const int MostRead = 20_000;

    private const string Project = ".csproj";

    private readonly ConcurrentDictionary<string, Search> _searched = new(StringComparer.Ordinal);

    /// <summary>How long an output of a scratchpad must have gone unwritten before it is offered.</summary>
    public static TimeSpan Quiet { get; } = TimeSpan.FromHours(2);

    private readonly TimeSpan _quiet = quiet ?? Quiet;

    /// <summary>How long one search of a scratchpad, with its judgements, is taken as read.</summary>
    public static TimeSpan Searched { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The outputs in a scratchpad, each with what it is and why it is to be left now, if it is.</summary>
    public IReadOnlyList<Output> In(string scratchpad, IReadOnlyList<RunningProcess> running)
    {
        var now = clock.GetUtcNow();

        if (!_searched.TryGetValue(scratchpad, out var search) || now - search.At >= Searched)
        {
            search = new Search(now, [.. Find(scratchpad).Select(found => new Output(found.Path, found.What, Written(found.Path)))]);
            _searched[scratchpad] = search;
            Forget(now);
        }

        return [.. search.Found.Select(output => output with { Blocker = output.Blocker ?? Started(output.Path, running) })];
    }

    /// <summary>Why an output is to be left right now; null when nothing was written in it lately and no running program was started from it.</summary>
    public string? Busy(string path, IReadOnlyList<RunningProcess> running) => Written(path) ?? Started(path, running);

    /// <summary>Why an output may be in a build's hands: written lately, or too large to tell cheaply.</summary>
    private string? Written(string path)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

        try
        {
            var newest = Directory.GetLastWriteTimeUtc(path);
            var read = 0;

            foreach (var written in new FileSystemEnumerable<DateTime>(path, static (ref entry) => entry.LastWriteTimeUtc.UtcDateTime, options))
            {
                if (++read > MostRead)
                    return $"more than {MostRead} entries: when it was last written cannot be told cheaply, so a build may still be using it";

                if (written > newest)
                    newest = written;
            }

            var since = clock.GetUtcNow().UtcDateTime - newest;

            return since < _quiet ? $"written {Ago(since)} ago: {(_quiet == Quiet ? "a build may still be using it" : $"its project was worked on in the last {Ago(_quiet)}")}" : null;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return $"could not be read: {refused.Message}";
        }
    }

    /// <summary>A span the way a person says it: minutes, hours or days.</summary>
    private static string Ago(TimeSpan span) => span switch
    {
        _ when span < TimeSpan.FromHours(2) => $"{Math.Max(span.TotalMinutes, 0):0} minutes",
        _ when span < TimeSpan.FromDays(2) => $"{span.TotalHours:0} hours",
        _ => $"{span.TotalDays:0} days"
    };

    private static string? Started(string path, IReadOnlyList<RunningProcess> running)
    {
        var inside = $"{path}/";
        var program = running.FirstOrDefault(process =>
            (process.Executable?.StartsWith(inside, StringComparison.Ordinal) ?? false) || process.Command.Contains(inside, StringComparison.Ordinal));

        return program is null ? null : $"{program.Program} ({program.Pid}) runs from it";
    }

    /// <summary>The outputs under a scratchpad, breadth first, past hidden folders, links, Python environments and folders of tools.</summary>
    private static List<(string Path, string What)> Find(string scratchpad)
    {
        var found = new List<(string Path, string What)>();
        var waiting = new Queue<(string Path, int Depth)>([(scratchpad, 0)]);
        var looked = 0;

        while (waiting.TryDequeue(out var directory) && ++looked <= MostDirectories)
        {
            var entries = Entries(directory.Path);

            if (entries.Exists(entry => !entry.IsDirectory && entry.Name == "pyvenv.cfg") || entries.Exists(entry => entry.IsDirectory && entry.Name == ".store"))
                continue;

            var isProject = entries.Exists(entry => !entry.IsDirectory && entry.Name.EndsWith(Project, StringComparison.Ordinal));
            var isPackage = entries.Exists(entry => !entry.IsDirectory && entry.Name == "package.json");

            foreach (var (name, isDirectory) in entries)
            {
                if (!isDirectory || name.StartsWith('.'))
                    continue;

                var path = Path.Combine(directory.Path, name);

                if (name is "TestResults" or "BenchmarkDotNet.Artifacts" || (isProject && name is "bin" or "obj") || (isPackage && name == "node_modules"))
                    found.Add((path, name));
                else if (name != "node_modules" && directory.Depth + 1 < Deepest)
                    waiting.Enqueue((path, directory.Depth + 1));
            }
        }

        return found;
    }

    /// <summary>The entries of a directory, each with whether it is a directory to go into: a real one, not a link to one.</summary>
    private static List<(string Name, bool IsDirectory)> Entries(string directory)
    {
        try
        {
            return
            [
                .. new FileSystemEnumerable<(string, bool)>(
                    directory,
                    static (ref entry) => (entry.FileName.ToString(), entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0),
                    Listing)
            ];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    /// <summary>Drops the searches of scratchpads nobody asked about for a while.</summary>
    private void Forget(DateTimeOffset now)
    {
        foreach (var (scratchpad, search) in _searched)
        {
            if (now - search.At >= Searched * 3)
                _searched.TryRemove(scratchpad, out _);
        }
    }

    /// <summary>One output: where it is, what it is, and why it is to be left now, if it is.</summary>
    /// <param name="Path">The folder.</param>
    /// <param name="What">Its name: <c>bin</c>, <c>obj</c>, <c>TestResults</c>, <c>BenchmarkDotNet.Artifacts</c> or <c>node_modules</c>.</param>
    /// <param name="Blocker">Why it is to be left now; null when nothing stands in the way.</param>
    public sealed record Output(string Path, string What, string? Blocker);

    private sealed record Search(DateTimeOffset At, IReadOnlyList<Output> Found);
}
