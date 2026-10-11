using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Files and directories: the scratchpads of sessions, and whatever a rule says is a known leftover or a cache.</summary>
/// <remarks>
/// Claude Code gives every session a scratchpad at <c>claude-&lt;uid&gt;/&lt;project&gt;/&lt;session&gt;</c>, so the
/// path itself carries the owner. A scratchpad is never released while its session runs — the session
/// writes into it — and once the session is over, it goes to the Trash rather than nowhere, because what an
/// agent wrote there may be the only copy of something.
/// </remarks>
public sealed class FileKind(ServerConfig config, FileTrail trail, IRules rules, IProcessTable table, Disposal disposal, TimeProvider clock) : IResourceKind
{
    /// <summary>
    /// How recently a scratchpad may have been written and still be in use, whatever the sessions say. A session
    /// SleepOf never heard of is known only by its process, under another name than its scratchpad's — and
    /// its scratchpad must not look orphaned for it.
    /// </summary>
    public static TimeSpan InUse { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc/>
    public string Name => Kinds.File;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var known = await rules.ListAsync(cancellationToken);
        var running = (await table.ListAsync(cancellationToken)).Select(process => process.Program).ToHashSet(StringComparer.Ordinal);
        var traces = await trail.FollowAsync(known, cancellationToken);

        List<Resource> found = [.. Scratchpads(sessions), .. Ruled(traces, known, running)];

        return new Observation(Name, found, []);
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
        Task.FromResult(disposal.Dispose(resource, resource.Key));

    private IEnumerable<Resource> Scratchpads(IReadOnlyList<Session> sessions)
    {
        var alive = sessions.Where(session => session.IsAlive).Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var roots = Directories(config.ScratchRoot).Where(root => Path.GetFileName(root).StartsWith("claude-", StringComparison.Ordinal));

        foreach (var project in roots.SelectMany(Directories))
        {
            foreach (var scratchpad in Directories(project).Where(directory => Guid.TryParse(Path.GetFileName(directory), out _)))
            {
                var session = Path.GetFileName(scratchpad);
                var written = clock.GetUtcNow().UtcDateTime - LastWritten(scratchpad);

                yield return new Resource
                {
                    Kind = Name,
                    Key = scratchpad,
                    Title = $"scratchpad of session {session} in {Path.GetFileName(project)}",
                    Owner = Owner.Session(session),
                    Evidence = Evidence.Label,
                    Bytes = Sizes.Of(scratchpad),
                    CreatedAt = Directory.GetCreationTimeUtc(scratchpad),
                    Blocker = alive.Contains(session)
                        ? "its session is still running and writes into it"
                        : written < InUse ? $"written {written.TotalMinutes:0} minutes ago: its session may still be running unseen" : null
                };
            }
        }
    }

    /// <summary>The traces a rule names as a known leftover or a cache; the last rule that matches decides, so a learned one overrides a shipped one.</summary>
    private IEnumerable<Resource> Ruled(IReadOnlyList<Trace> traces, IReadOnlyList<Rule> known, HashSet<string> running)
    {
        foreach (var trace in traces)
        {
            var rule = known.LastOrDefault(candidate => candidate.Matches(trace));

            if (rule is null || rule.Action == RuleAction.Ignore)
                continue;

            var isCache = rule.Action == RuleAction.Cache;

            yield return new Resource
            {
                Kind = Name,
                Key = trace.Key,
                Title = rule.Note is null ? trace.Title : $"{trace.Title} — {rule.Note}",
                IsShared = isCache,
                IsRegenerable = isCache,
                Bytes = Sizes.Of(trace.Key),
                Blocker = rule.BlockedWhileRunning is { } program && running.Contains(program) ? $"{program} is running and keeps it open" : null
            };
        }
    }

    /// <summary>The newest write under a directory, looking a few levels down and no further.</summary>
    private static DateTime LastWritten(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };

        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options).Take(5_000).Select(entry => entry.LastWriteTimeUtc).Append(Directory.GetLastWriteTimeUtc(directory)).Max();
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return Directory.GetLastWriteTimeUtc(directory);
        }
    }

    private static IEnumerable<string> Directories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? [.. Directory.EnumerateDirectories(directory)] : [];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
