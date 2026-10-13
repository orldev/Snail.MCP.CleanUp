using System.IO.Enumeration;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Files and directories: the scratchpads of sessions, and whatever a rule says is a known leftover or a cache.</summary>
/// <remarks>
/// Claude Code gives every session a scratchpad at <c>claude-&lt;uid&gt;/&lt;project&gt;/&lt;session&gt;</c>, so the
/// path itself carries the owner. A scratchpad is never released while its session runs — the session
/// writes into it — and once the session is over, it goes to the Trash rather than nowhere, because what an
/// agent wrote there may be the only copy of something. What a build makes again inside the scratchpad of a running
/// session is offered to that session alone, each output on its own (see <see cref="BuildOutputs"/>).
/// </remarks>
public sealed class FileKind(ServerConfig config, FileTrail trail, IRules rules, IProcessTable table, Disposal disposal, Sizes sizes, TimeProvider clock) : IResourceKind
{
    /// <summary>
    /// How recently a scratchpad may have been written and still be in use, whatever the sessions say. A session
    /// SleepOf never heard of is known only by its process, under another name than its scratchpad's — and
    /// its scratchpad must not look orphaned for it.
    /// </summary>
    public static TimeSpan InUse { get; } = TimeSpan.FromHours(1);

    private readonly BuildOutputs _outputs = new(clock);

    /// <inheritdoc/>
    public string Name => Kinds.File;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var known = await rules.ListAsync(cancellationToken);
        var processes = await table.ListAsync(cancellationToken);
        var running = processes.Select(process => process.Program).ToHashSet(StringComparer.Ordinal);
        var traces = await trail.FollowAsync(known, cancellationToken);

        List<Resource> found = [.. Scratchpads(sessions, processes), .. Ruled(traces, known, running)];

        return new Observation(Name, found, []) { Followed = new Walk(trail.Name, traces) };
    }

    /// <inheritdoc/>
    /// <remarks>An output of a running session's scratchpad is judged again first: a build may have begun since the plan was shown.</remarks>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (resource.Facts.ContainsKey(OutputFact) && Directory.Exists(resource.Key))
        {
            table.Refresh();

            if (_outputs.Busy(resource.Key, await table.ListAsync(cancellationToken)) is { } busy)
                return Release.NotTheSame(resource, busy);
        }

        return disposal.Dispose(resource, resource.Key);
    }

    /// <summary>The fact a scratchpad carries its session's id under.</summary>
    public const string SessionFact = "session";

    /// <summary>The fact an output of a scratchpad carries what it is under; its scratchpad is under <see cref="ScratchpadFact"/>.</summary>
    public const string OutputFact = "output";

    /// <summary>The fact an output carries the scratchpad it lies in under.</summary>
    public const string ScratchpadFact = "scratchpad";

    private IEnumerable<Resource> Scratchpads(IReadOnlyList<Session> sessions, IReadOnlyList<RunningProcess> running)
    {
        var alive = sessions.Where(session => session.IsAlive).Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var roots = Directories(config.ScratchRoot).Where(root => Path.GetFileName(root).StartsWith("claude-", StringComparison.Ordinal));

        foreach (var project in roots.SelectMany(Directories))
        {
            foreach (var scratchpad in Directories(project).Where(directory => Guid.TryParse(Path.GetFileName(directory), out _)))
            {
                var session = Path.GetFileName(scratchpad);
                var written = clock.GetUtcNow().UtcDateTime - LastWritten(scratchpad);
                var size = sizes.Of(scratchpad);

                yield return new Resource
                {
                    Kind = Name,
                    Key = scratchpad,
                    Title = $"scratchpad of session {session} in {Path.GetFileName(project)}",
                    Owner = Owner.Session(session),
                    Evidence = Evidence.Label,
                    Bytes = size?.Bytes,
                    IsAtLeast = size is { IsAtLeast: true },
                    CreatedAt = Directory.GetCreationTimeUtc(scratchpad),
                    Blocker = alive.Contains(session)
                        ? "its session is still running and writes into it"
                        : written < InUse ? $"written {written.TotalMinutes:0} minutes ago: its session may still be running unseen" : null,
                    Facts = new Dictionary<string, string> { [SessionFact] = session }
                };

                if (!alive.Contains(session))
                    continue;

                foreach (var output in _outputs.In(scratchpad, running))
                    yield return Output(scratchpad, session, output);
            }
        }
    }

    /// <summary>An output of a running session's scratchpad: the session's by the scratchpad's name, and made again by the next build.</summary>
    private Resource Output(string scratchpad, string session, BuildOutputs.Output output)
    {
        var size = sizes.Of(output.Path);

        return new Resource
        {
            Kind = Name,
            Key = output.Path,
            Title = $"{Path.GetRelativePath(scratchpad, output.Path)} in the scratchpad of session {session}: {output.What} the next build, restore or test run makes again",
            Owner = Owner.Session(session),
            Evidence = Evidence.Label,
            Bytes = size?.Bytes,
            IsAtLeast = size is { IsAtLeast: true },
            IsRegenerable = true,
            Blocker = output.Blocker,
            Facts = new Dictionary<string, string> { [OutputFact] = output.What, [ScratchpadFact] = scratchpad }
        };
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
            var size = sizes.Of(trace.Key);

            yield return new Resource
            {
                Kind = Name,
                Key = trace.Key,
                Title = rule.Note is null ? trace.Title : $"{trace.Title} — {rule.Note}",
                IsShared = isCache,
                IsRegenerable = isCache,
                Bytes = size?.Bytes,
                IsAtLeast = size is { IsAtLeast: true },
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
            var writes = new FileSystemEnumerable<DateTime>(directory, (ref entry) => entry.LastWriteTimeUtc.UtcDateTime, options);

            return writes.Take(5_000).Append(Directory.GetLastWriteTimeUtc(directory)).Max();
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
