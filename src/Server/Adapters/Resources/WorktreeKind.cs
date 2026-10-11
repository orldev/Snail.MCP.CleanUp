namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Git worktrees of the repositories agents work in: named, never removed.</summary>
/// <remarks>
/// A worktree holds a branch and maybe uncommitted work, and nothing in it says which agent made it. Claude
/// Code removes the worktrees of its subagents that changed nothing; the ones left behind are left on
/// purpose or by accident, and telling which takes a person. So they are listed with their branch, and with
/// <c>prunable</c> when their directory is already gone and only git's record of them remains.
/// </remarks>
public sealed class WorktreeKind(IPrograms programs) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Worktree;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, Resource>(StringComparer.Ordinal);
        var directories = sessions.Select(session => session.Directory).OfType<string>().Where(Directory.Exists).Distinct(StringComparer.Ordinal);

        foreach (var directory in directories)
        {
            var run = await programs.RunAsync("git", ["-C", directory, "worktree", "list", "--porcelain"], TimeSpan.FromSeconds(10), cancellationToken);

            if (!run.IsSuccess)
                continue;

            foreach (var worktree in Parse(run.Output).Skip(1))
                found.TryAdd(worktree.Key, worktree);
        }

        return new Observation(Name, [.. found.Values], [.. found.Keys.Select(path => $"file:{path}")]);
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
        Task.FromResult(Release.Refused(resource, "a worktree may hold work nobody committed; remove it with git worktree remove once a person has looked"));

    /// <summary>The worktrees of <c>git worktree list --porcelain</c>, the main one first.</summary>
    internal static IReadOnlyList<Resource> Parse(string output) =>
    [
        .. output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(block =>
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries);
            var path = Field(lines, "worktree ") ?? string.Empty;
            var branch = Field(lines, "branch ")?.Replace("refs/heads/", string.Empty, StringComparison.Ordinal) ?? "detached";
            var isPrunable = lines.Any(line => line.StartsWith("prunable", StringComparison.Ordinal));

            return new Resource
            {
                Kind = Kinds.Worktree,
                Key = path,
                Title = isPrunable ? $"worktree {path} on {branch}, prunable: its directory is gone" : $"worktree {path} on {branch}",
                Facts = new Dictionary<string, string> { ["branch"] = branch, ["prunable"] = isPrunable ? "yes" : "no" }
            };
        })
    ];

    private static string? Field(string[] lines, string name) =>
        lines.FirstOrDefault(line => line.StartsWith(name, StringComparison.Ordinal))?[name.Length..];
}
