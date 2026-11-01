using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>The transcripts of Claude Code sessions nobody wrote into for <see cref="ServerConfig.HistoryDays"/>: a session's <c>.jsonl</c> and the folder of its subagents and tool results.</summary>
/// <remarks>
/// Claude Code writes every session to <c>~/.claude/projects/&lt;working directory&gt;/&lt;session&gt;.jsonl</c>, with the
/// transcripts of its subagents and the tool results too large to keep inline in a <c>&lt;session&gt;</c> folder beside
/// it. Its own <c>cleanupPeriodDays</c> is thirty days by default, yet transcripts from 2026-08-02 were still there on
/// 2026-11-02: 123 of them, 11.5 GB, one session alone 2.4 GB. A transcript is the record of a conversation rather than a
/// leftover, so it is the session's by its name and goes only in a plan somebody asked for and agreed to — never in a
/// sweep of the server's own (<see cref="MachineKinds.IsLeftUntilAsked"/>). Agreed to, it is erased: the Trash would give
/// back no space. It is judged again right before it goes, since a session resumed since the plan writes into it.
/// </remarks>
public sealed class HistoryKind(ServerConfig config, Sizes sizes, TimeProvider clock) : IResourceKind
{
    private readonly TimeSpan _stale = TimeSpan.FromDays(config.HistoryDays);

    /// <summary>The fact a transcript carries its project folder under.</summary>
    public const string ProjectFact = "project";

    private const string Transcript = ".jsonl";

    /// <inheritdoc/>
    public string Name => MachineKinds.History;

    /// <inheritdoc/>
    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        var alive = holders.OfType<Session>().Where(session => session.IsAlive).Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var found = new List<Resource>();

        foreach (var (project, session) in Transcripts())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!alive.Contains(session) && Described(project, session) is { } resource)
                found.Add(resource);
        }

        return Task.FromResult(new Observation(Name, found, []));
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var folder = FolderOf(resource.Key);

        if (!File.Exists(resource.Key) && !Directory.Exists(folder))
            return Task.FromResult(Release.AlreadyGone(resource));

        if (Quiet(resource.Key) < _stale)
            return Task.FromResult(Release.NotTheSame(resource, "its session was written into since the plan: it was resumed"));

        try
        {
            File.Delete(resource.Key);

            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Release.Refused(resource, failure.Message));
        }

        return Task.FromResult(Release.Done(resource, "erased: the person agreed to it in a plan, and the Trash would give back no space"));
    }

    private Resource? Described(string project, string session)
    {
        var transcript = Path.Combine(project, $"{session}{Transcript}");
        var quiet = Quiet(transcript);

        if (quiet < _stale)
            return null;

        var file = sizes.Of(transcript);
        var folder = Directory.Exists(FolderOf(transcript)) ? sizes.Of(FolderOf(transcript)) : null;

        var resource = new Resource
        {
            Kind = Name,
            Key = transcript,
            Title = $"transcript of session {session} in {Path.GetFileName(project)}, nobody wrote into it for {quiet.TotalDays:0} days; /resume will no longer find it",
            Owner = Owner.Session(session),
            Evidence = Evidence.Label,
            Bytes = (file?.Bytes ?? 0) + (folder?.Bytes ?? 0),
            IsAtLeast = folder is { IsAtLeast: true },
            CreatedAt = File.GetCreationTimeUtc(transcript),
            Facts = new Dictionary<string, string> { [ProjectFact] = Path.GetFileName(project) }
        };

        return resource;
    }

    /// <summary>Every session transcript under the projects folder, as its project folder and its session id.</summary>
    private IEnumerable<(string Project, string Session)> Transcripts()
    {
        foreach (var project in Entries(config.ClaudeProjects, Directory.EnumerateDirectories))
        {
            foreach (var transcript in Entries(project, folder => Directory.EnumerateFiles(folder, $"*{Transcript}")))
            {
                var session = Path.GetFileNameWithoutExtension(transcript);

                if (Guid.TryParse(session, out _))
                    yield return (project, session);
            }
        }
    }

    /// <summary>How long nobody wrote into a transcript or its folder.</summary>
    private TimeSpan Quiet(string transcript)
    {
        var folder = FolderOf(transcript);
        var written = File.Exists(transcript) ? File.GetLastWriteTimeUtc(transcript) : DateTime.MinValue;

        if (Directory.Exists(folder) && Directory.GetLastWriteTimeUtc(folder) > written)
            written = Directory.GetLastWriteTimeUtc(folder);

        return clock.GetUtcNow().UtcDateTime - written;
    }

    private static string FolderOf(string transcript) => transcript[..^Transcript.Length];

    private static IReadOnlyList<string> Entries(string directory, Func<string, IEnumerable<string>> listed)
    {
        try
        {
            return Directory.Exists(directory) ? [.. listed(directory)] : [];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
