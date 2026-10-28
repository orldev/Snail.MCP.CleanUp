using System.Globalization;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>The build outputs of the repositories agents and the developer work in: <c>bin</c> and <c>obj</c> no build wrote into for a week.</summary>
/// <remarks>
/// Every build, test run and publish an agent starts in a repository writes a tree per configuration, framework and runtime
/// into its <c>bin</c> and <c>obj</c>, and nothing removes them: 238 of them held 39.6 GB on 2026-10-28. They lie in the
/// developer's own repositories, so the server never clears them on its own; a plan of caches offers them, one entry per
/// repository, with the folders in it no build wrote into for <see cref="Quiet"/> and no running program was started from.
/// The next build makes them again. Each folder is judged again right before it goes, and a folder of a repository inside
/// another is that inner repository's alone.
/// </remarks>
public sealed class ProjectOutputsKind(Repositories repositories, IProcessTable table, Disposal disposal, Sizes sizes, TimeProvider clock) : IResourceKind
{
    /// <summary>The fact a repository's entry carries how many folders it offers under.</summary>
    public const string FoldersFact = "folders";

    private static readonly HashSet<string> Offered = new(["bin", "obj"], StringComparer.Ordinal);

    private readonly BuildOutputs _outputs = new(clock, Quiet);

    /// <summary>How long a repository's outputs must have gone unwritten before they are offered: its project is not being worked on.</summary>
    public static TimeSpan Quiet { get; } = TimeSpan.FromDays(7);

    /// <inheritdoc/>
    public string Name => MachineKinds.ProjectOutputs;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        var running = await table.ListAsync(cancellationToken);
        var found = new List<Resource>();

        foreach (var (repository, outputs) in Owned(repositories.Named(Repositories.Worked(holders)), running))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Described(repository, outputs) is { } resource)
                found.Add(resource);
        }

        return new Observation(Name, found, []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(resource.Key))
            return Release.AlreadyGone(resource);

        table.Refresh();

        var running = await table.ListAsync(cancellationToken);
        var erased = 0;
        string? left = null;

        foreach (var output in _outputs.In(resource.Key, running).Where(output => Offered.Contains(output.What)))
        {
            if (_outputs.Busy(output.Path, running) is { } busy)
            {
                left ??= $"{Path.GetRelativePath(resource.Key, output.Path)} {busy}";
                continue;
            }

            var disposed = disposal.Dispose(resource, output.Path);

            if (disposed.Outcome == Outcome.Released)
                erased++;
            else if (disposed.Outcome != Outcome.Gone)
                left ??= $"{Path.GetRelativePath(resource.Key, output.Path)}: {disposed.Detail}";
        }

        if (erased == 0)
            return left is null ? Release.AlreadyGone(resource) : Release.NotTheSame(resource, left);

        var rest = left is null ? string.Empty : $"; the rest was left: {left}";

        return Release.Done(resource, $"erased {erased} bin and obj folder(s), which the next build makes again{rest}");
    }

    /// <summary>The quiet outputs of each repository, deepest repositories first, so that a folder is only its innermost repository's.</summary>
    private IEnumerable<(string Repository, IReadOnlyList<BuildOutputs.Output> Outputs)> Owned(IReadOnlyList<string> named, IReadOnlyList<RunningProcess> running)
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var repository in named.OrderByDescending(path => path.Length))
        {
            var outputs = new List<BuildOutputs.Output>();

            foreach (var output in _outputs.In(repository, running))
            {
                if (Offered.Contains(output.What) && claimed.Add(output.Path) && output.Blocker is null)
                    outputs.Add(output);
            }

            yield return (repository, outputs);
        }
    }

    private Resource? Described(string repository, IReadOnlyList<BuildOutputs.Output> outputs)
    {
        if (outputs.Count == 0)
            return null;

        long bytes = 0;
        var isAtLeast = false;

        foreach (var output in outputs)
        {
            var size = sizes.Of(output.Path);

            bytes += size?.Bytes ?? 0;
            isAtLeast |= size is null or { IsAtLeast: true };
        }

        if (bytes == 0 && !isAtLeast)
            return null;

        return new Resource
        {
            Kind = Name,
            Key = repository,
            Title = $"build outputs of {Path.GetFileName(repository)}: {outputs.Count} bin and obj folder(s) no build wrote into for {Quiet.TotalDays:0} days; the next build makes them again",
            IsShared = true,
            IsRegenerable = true,
            Bytes = bytes,
            IsAtLeast = isAtLeast,
            Facts = new Dictionary<string, string> { [FoldersFact] = outputs.Count.ToString(CultureInfo.InvariantCulture) }
        };
    }
}
