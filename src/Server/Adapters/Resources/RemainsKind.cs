using System.Globalization;
using System.IO.Enumeration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>What processes that have ended left in the temporary folder: the debugger pipes and diagnostic sockets of .NET, the folders of test runs.</summary>
/// <remarks>
/// Every .NET process opens <c>clr-debug-pipe-&lt;pid&gt;-&lt;start&gt;-in</c> and <c>-out</c> for a debugger and
/// <c>dotnet-diagnostic-&lt;pid&gt;-&lt;start&gt;-socket</c> for the diagnostic tools, and a process that is killed or
/// crashes never removes them: a Mac's temporary folder held 3 939 of each on 2026-10-13, and the census wrote down 87 161
/// discoveries, most of them these. The name carries the process and the second it started, so whether the owner is gone
/// is a fact rather than a guess: no process has that id, or the one that has it started at another second — the id was
/// given again. An endpoint of a process that is gone reaches nobody and is nobody's, so the lot of them is one cache,
/// removed when caches are asked for; one of a process still running is never touched.
/// <para>
/// The test runs of the Snail libraries and servers name their temporary folders the same way:
/// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c> (Snail.Sdk.Mcp.Testing), and a run that is killed leaves its folder
/// — tens of thousands of them were discoveries on 2026-10-13. They are judged as the endpoints are, and are a second
/// cache of this kind, with their size.
/// </para>
/// <para>
/// The file trail does not list either (<see cref="IsRemains"/>): what this kind accounts for is no discovery, and tens of
/// thousands of traces a census were the larger part of its allocations.
/// </para>
/// </remarks>
/// <param name="table">The processes running now, which say whose endpoints are still in use.</param>
/// <param name="sizes">How the size of a test run's folder is counted.</param>
/// <param name="clock">What time it is.</param>
/// <param name="directory">The temporary folder the runtime writes its endpoints into.</param>
public sealed class RemainsKind(IProcessTable table, Sizes sizes, TimeProvider clock, string directory) : IResourceKind
{
    private const string DebuggerPipe = "clr-debug-pipe-";

    private const string DiagnosticSocket = "dotnet-diagnostic-";

    private const string TestRun = "s3l-test-";

    /// <summary>How many folders of ended test runs one census counts the size of; past them the size is «at least».</summary>
    public const int MostMeasured = 2_000;

    /// <summary>How long after its process started an endpoint is left alone whatever the table says: the table may be read from before it.</summary>
    public static TimeSpan Young { get; } = TimeSpan.FromMinutes(2);

    /// <summary>The kind over the temporary folder of this user, the one the runtime writes into.</summary>
    public RemainsKind(IProcessTable table, Sizes sizes, TimeProvider clock)
        : this(table, sizes, clock, Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
    {
    }

    /// <summary>The key of the folders of test runs that have ended: the pattern of their names in the folder.</summary>
    private string TestRunsKey => Path.Combine(directory, $"{TestRun}*");

    /// <inheritdoc/>
    public string Name => Kinds.Remains;

    /// <summary>Whether a name is one the runtime gives an endpoint of a process, read without making a string of it.</summary>
    public static bool IsEndpoint(ReadOnlySpan<char> name) => Endpoint(name) is not null;

    /// <summary>Whether a name is one this kind accounts for: an endpoint's, or a test run's folder's.</summary>
    public static bool IsRemains(ReadOnlySpan<char> name) => Endpoint(name) is not null || TestRoot(name) is not null;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);

        if (running.Count == 0)
            return Observation.Unseen(Name, "the process table could not be read, so no process can be proven to have ended");

        var alive = Alive(running);
        var (ended, kept) = Sorted(alive);
        var found = new List<Resource>(2);

        if (ended.Files > 0)
        {
            found.Add(new Resource
            {
                Kind = Name,
                Key = directory,
                Title = $"debugger pipes and diagnostic sockets of {ended.Processes} .NET process(es) that have ended, {ended.Files} file(s) in {directory}; "
                    + $"those of {kept.Processes} running process(es) are left alone",
                IsShared = true,
                IsRegenerable = true,
                Facts = new Dictionary<string, string> { ["files"] = ended.Files.ToString(CultureInfo.InvariantCulture) }
            });
        }

        if (TestRuns(alive) is { } runs)
            found.Add(runs);

        return new Observation(Name, found, []);
    }

    /// <summary>The folders of the test runs that have ended, as one cache with their size; null when there are none.</summary>
    private Resource? TestRuns(Dictionary<int, long?> alive)
    {
        var (ended, running) = (new List<string>(), 0);

        foreach (var (path, pid, started) in TestRoots())
        {
            if (IsGone(pid, started, alive))
                ended.Add(path);
            else
                running++;
        }

        if (ended.Count == 0)
            return null;

        var (bytes, isAtLeast) = (0L, ended.Count > MostMeasured);

        foreach (var path in ended.Take(MostMeasured))
        {
            var size = sizes.Of(path);

            bytes += size?.Bytes ?? 0;
            isAtLeast |= size is { IsAtLeast: true };
        }

        return new Resource
        {
            Kind = Name,
            Key = TestRunsKey,
            Title = $"temporary folders of {ended.Count} test run(s) that have ended, s3l-test-<pid>-<start>-* in {directory}; "
                + $"those of {running} running test run(s) are left alone",
            IsShared = true,
            IsRegenerable = true,
            Bytes = bytes,
            IsAtLeast = isAtLeast,
            Facts = new Dictionary<string, string> { ["folders"] = ended.Count.ToString(CultureInfo.InvariantCulture) }
        };
    }

    /// <inheritdoc/>
    /// <remarks>The table is read afresh and every endpoint judged again: what started since the plan was shown is not its business.</remarks>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        table.Refresh();

        var running = await table.ListAsync(cancellationToken);

        if (running.Count == 0)
            return Release.Refused(resource, "the process table could not be read, so no process can be proven to have ended");

        var alive = Alive(running);
        var isTestRuns = string.Equals(resource.Key, TestRunsKey, StringComparison.Ordinal);
        var removed = 0;
        var refused = new List<string>();
        var listed = isTestRuns
            ? TestRoots()
            : Listed(static (ref entry) => Endpoint(entry.FileName) is { } named ? (entry.ToFullPath(), named.Pid, named.Started) : default);

        foreach (var (path, pid, started) in listed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsGone(pid, started, alive))
                continue;

            try
            {
                if (isTestRuns)
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);

                removed++;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{Path.GetFileName(path)}: {failure.Message}");
            }
        }

        if (removed == 0 && refused.Count == 0)
            return Release.AlreadyGone(resource);

        if (removed == 0)
            return Release.Refused(resource, string.Join("; ", refused.Take(3)));

        var detail = isTestRuns ? $"erased the folders of {removed} test run(s) that have ended" : $"erased {removed} endpoint(s) of .NET processes that have ended";

        return Release.Done(resource, refused.Count == 0 ? detail : $"{detail}; {refused.Count} could not be: {string.Join("; ", refused.Take(3))}");
    }

    /// <summary>How many processes and files there are of the ended and of the running ones.</summary>
    private ((int Processes, int Files) Ended, (int Processes, int Files) Kept) Sorted(Dictionary<int, long?> alive)
    {
        var ended = new HashSet<(int, long)>();
        var kept = new HashSet<(int, long)>();
        var files = (Ended: 0, Kept: 0);

        foreach (var (pid, started) in Listed(static (ref entry) => Endpoint(entry.FileName).GetValueOrDefault()))
        {
            if (IsGone(pid, started, alive))
            {
                ended.Add((pid, started));
                files.Ended++;
            }
            else
            {
                kept.Add((pid, started));
                files.Kept++;
            }
        }

        return ((ended.Count, files.Ended), (kept.Count, files.Kept));
    }

    /// <summary>
    /// Whether the process an endpoint names is gone: no process has its id, or the one that has it started at another
    /// second. A process whose start the table does not know — the system's, another user's — is taken to be the one.
    /// </summary>
    private bool IsGone(int pid, long started, Dictionary<int, long?> alive)
    {
        if (clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(started) < Young)
            return false;

        if (!alive.TryGetValue(pid, out var start))
            return true;

        return start is { } known && Math.Abs(known - started) > 1;
    }

    private static Dictionary<int, long?> Alive(IReadOnlyList<RunningProcess> running)
    {
        var alive = new Dictionary<int, long?>(running.Count);

        foreach (var process in running)
            alive[process.Pid] = process.StartedAt == DateTimeOffset.UnixEpoch ? null : process.StartedAt.ToUnixTimeSeconds();

        return alive;
    }

    /// <summary>The endpoints in the folder, each as <paramref name="described"/> makes it; the other entries are passed over without a string made of them.</summary>
    private List<T> Listed<T>(FileSystemEnumerable<T>.FindTransform described)
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return
            [
                .. new FileSystemEnumerable<T>(directory, described, Listing)
                {
                    ShouldIncludePredicate = static (ref entry) => !entry.IsDirectory && IsEndpoint(entry.FileName)
                }
            ];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    /// <summary>The folders of test runs in the folder with the process and start their names carry; links named so are passed over.</summary>
    private List<(string Path, int Pid, long Started)> TestRoots()
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return
            [
                .. new FileSystemEnumerable<(string, int, long)>(directory, static (ref entry) => TestRoot(entry.FileName) is { } named ? (entry.ToFullPath(), named.Pid, named.Started) : default, Listing)
                {
                    ShouldIncludePredicate = static (ref entry) => entry.IsDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0 && TestRoot(entry.FileName) is not null
                }
            ];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The process and start the name of a test run's folder carries, <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>; null when the name is not one.</summary>
    private static (int Pid, long Started)? TestRoot(ReadOnlySpan<char> name)
    {
        if (!name.StartsWith(TestRun, StringComparison.Ordinal))
            return null;

        var rest = name[TestRun.Length..];
        var first = rest.IndexOf('-');

        if (first <= 0)
            return null;

        var second = rest[(first + 1)..].IndexOf('-');

        if (second <= 0
            || first + second + 2 >= rest.Length
            || !int.TryParse(rest[..first], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(rest.Slice(first + 1, second), NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            || pid <= 0)
            return null;

        return (pid, started);
    }

    /// <summary>The process and start an endpoint's name carries; null when the name is not an endpoint's.</summary>
    private static (int Pid, long Started)? Endpoint(ReadOnlySpan<char> name)
    {
        ReadOnlySpan<char> rest;

        if (name.StartsWith(DebuggerPipe, StringComparison.Ordinal))
        {
            rest = name[DebuggerPipe.Length..];

            if (rest.EndsWith("-in", StringComparison.Ordinal))
                rest = rest[..^3];
            else if (rest.EndsWith("-out", StringComparison.Ordinal))
                rest = rest[..^4];
            else
                return null;
        }
        else if (name.StartsWith(DiagnosticSocket, StringComparison.Ordinal) && name.EndsWith("-socket", StringComparison.Ordinal))
        {
            rest = name[DiagnosticSocket.Length..^7];
        }
        else
        {
            return null;
        }

        var dash = rest.IndexOf('-');

        if (dash <= 0
            || !int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(rest[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            || pid <= 0)
            return null;

        return (pid, started);
    }
}
