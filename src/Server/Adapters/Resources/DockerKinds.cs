using System.Collections.Frozen;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Docker containers: owned when they carry the owner label, never otherwise.</summary>
/// <remarks>
/// Docker objects say nothing about who made them unless they were told to. A container started with
/// <c>--label snail.owner=…</c> is proof; one without it is named and left alone, however recently it
/// appeared — and the trail notices it as something new.
/// </remarks>
public sealed class ContainerKind(IDockerEngine engine) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Container;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken) =>
        await engine.SnapshotAsync(cancellationToken) is { } snapshot
            ? new Observation(Name, [.. snapshot.Containers.Select(Described)], [])
            : Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (await engine.ContainerAsync(resource.Key, cancellationToken) is not { } present)
            return Release.AlreadyGone(resource);

        if (Docker.OwnerOf(present.Labels) != resource.Owner && resource.Evidence == Evidence.Label)
            return Release.NotTheSame(resource, "its owner label changed since the plan was made");

        var removed = await engine.RemoveAsync("containers", resource.Key, cancellationToken);

        return removed.IsDone ? Release.Done(resource, $"removed container {present.Name}") : Release.Refused(resource, removed.Message);
    }

    private Resource Described(DockerContainer container)
    {
        var owner = Docker.OwnerOf(container.Labels);

        return new Resource
        {
            Kind = Name,
            Key = container.Id,
            Title = $"container {container.Name} ({container.Image}, {container.State})",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            Facts = new Dictionary<string, string> { ["name"] = container.Name }
        };
    }
}

/// <summary>Docker networks: owned by the label, released after every container that uses them.</summary>
public sealed class NetworkKind(IDockerEngine engine) : IResourceKind
{
    private static readonly FrozenSet<string> Builtin = FrozenSet.Create(StringComparer.Ordinal, "bridge", "host", "none");

    /// <inheritdoc/>
    public string Name => Kinds.Network;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

        return new Observation(Name, [.. snapshot.Networks.Where(network => !Builtin.Contains(network.Name)).Select(network => Described(network, snapshot.Containers))], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await engine.RemoveAsync("networks", resource.Key, cancellationToken);

        if (removed.IsDone)
            return Release.Done(resource, $"removed network {resource.Facts["name"]}");

        return removed.IsMissing ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Message);
    }

    private Resource Described(DockerNetwork network, IReadOnlyList<DockerContainer> containers)
    {
        var owner = Docker.OwnerOf(network.Labels);

        return new Resource
        {
            Kind = Name,
            Key = network.Id,
            Title = $"network {network.Name}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            After = [.. containers.Where(container => container.Networks.Contains(network.Name, StringComparer.Ordinal)).Select(container => $"{Kinds.Container}:{container.Id}")],
            Facts = new Dictionary<string, string> { ["name"] = network.Name }
        };
    }
}

/// <summary>Docker volumes: the one Docker object that holds work, so it goes only on its label and only after its containers.</summary>
public sealed class VolumeKind(IDockerEngine engine) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Volume;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

        return new Observation(Name, [.. snapshot.Volumes.Select(volume => Described(volume, snapshot.Containers))], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await engine.RemoveAsync("volumes", resource.Key, cancellationToken);

        if (removed.IsDone)
            return Release.Done(resource, $"removed volume {resource.Key}");

        return removed.IsMissing ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Message);
    }

    private Resource Described(DockerVolume volume, IReadOnlyList<DockerContainer> containers)
    {
        var owner = Docker.OwnerOf(volume.Labels);

        return new Resource
        {
            Kind = Name,
            Key = volume.Name,
            Title = $"volume {volume.Name}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            After = [.. containers.Where(container => container.Volumes.Contains(volume.Name, StringComparer.Ordinal)).Select(container => $"{Kinds.Container}:{container.Id}")]
        };
    }
}

/// <summary>Docker images built with the owner label.</summary>
public sealed class ImageKind(IDockerEngine engine) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Image;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

        return new Observation(Name, [.. snapshot.Labelled.Select(Described)], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await engine.RemoveAsync("images", resource.Key, cancellationToken);

        if (removed.IsDone)
            return Release.Done(resource, $"removed {resource.Title}");

        return removed.IsMissing ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Message);
    }

    private Resource Described(DockerImage image)
    {
        var owner = Docker.OwnerOf(image.Labels);

        return new Resource
        {
            Kind = Name,
            Key = image.Id,
            Title = $"image {(image.Tags.Count > 0 ? image.Tags[0] : image.Id[..Math.Min(19, image.Id.Length)])}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            Bytes = image.Size
        };
    }
}

/// <summary>Docker's build cache, as one shared resource the size of what it could give back.</summary>
/// <remarks>
/// The build cache belongs to the machine, not to an agent, and rebuilds itself: it is a cache in every sense,
/// removed only when caches are asked for by name. It is also where the disk goes — a stand whose disk filled
/// stopped its database without a word — so its size is always in the inventory.
/// </remarks>
public sealed class BuildCacheKind(IDockerEngine engine) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => MachineKinds.BuildCache;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

        if (snapshot.BuildCacheReclaimable <= 0)
            return new Observation(Name, [], []);

        return new Observation(
            Name,
            [
                new Resource
                {
                    Kind = Name,
                    Key = "docker",
                    Title = $"Docker build cache, {Snail.MCP.CleanUp.Application.Views.Size(snapshot.BuildCacheReclaimable)} reclaimable",
                    IsShared = true,
                    IsRegenerable = true,
                    Bytes = snapshot.BuildCacheReclaimable
                }
            ],
            []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var pruned = await engine.PruneAsync("build", cancellationToken);

        return pruned.IsDone ? Release.Done(resource, "pruned the build cache; the next build fills what it needs") : Release.Refused(resource, pruned.Message);
    }
}

/// <summary>Images no tag points at any more: what every rebuild of a tag leaves of the build before it.</summary>
/// <remarks>
/// A compose stand rebuilt twenty times keeps twenty images of each service, nineteen of them called
/// <c>&lt;none&gt;</c>. They are nobody's on purpose and rebuild themselves, so they are one shared cache, removed only
/// when caches are asked for — by the prune of dangling images, which never touches an image a tag or a container still uses.
/// </remarks>
public sealed class DanglingImageKind(IDockerEngine engine) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => MachineKinds.DanglingImages;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return Observation.Unseen(Name, await engine.UnreachableAsync(cancellationToken) ?? "Docker could not be read");

        if (snapshot.Dangling.Count == 0)
            return new Observation(Name, [], []);

        return new Observation(
            Name,
            [
                new Resource
                {
                    Kind = Name,
                    Key = "docker",
                    Title = $"{snapshot.Dangling.Count} Docker image(s) no tag points at, left by rebuilds",
                    IsShared = true,
                    IsRegenerable = true,
                    Bytes = snapshot.Dangling.Sum(image => image.Size)
                }
            ],
            []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var pruned = await engine.PruneAsync("dangling", cancellationToken);

        return pruned.IsDone ? Release.Done(resource, "pruned the images no tag points at") : Release.Refused(resource, pruned.Message);
    }
}

/// <summary>BuildKit's records of finished builds older than a day: logs and provenance of builds nobody is looking into any more.</summary>
/// <remarks>
/// BuildKit keeps a record of every build — its log, its steps, its provenance — and trims them itself, by default
/// to fifty records and two days: measured on 2026-10-11 at exactly fifty on a Mac that builds a stand of seven
/// images. So they do not grow without end, and today's are what a failed build is read from. Only the older ones
/// are a cache, removed when caches are asked for. The records are BuildKit's, not the Engine's, so they are read
/// through the command line.
/// </remarks>
public sealed class BuildHistoryKind(IDockerEngine engine, Docker docker, TimeProvider clock) : IResourceKind
{
    /// <summary>How old a record must be before it counts as nobody's business any more.</summary>
    public static TimeSpan Kept { get; } = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public string Name => MachineKinds.BuildHistory;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (await engine.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var old = await OldAsync(cancellationToken);

        if (old.Count == 0)
            return new Observation(Name, [], []);

        return new Observation(
            Name,
            [
                new Resource
                {
                    Kind = Name,
                    Key = "docker",
                    Title = $"{old.Count} BuildKit record(s) of builds older than a day",
                    IsShared = true,
                    IsRegenerable = true
                }
            ],
            []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var old = await OldAsync(cancellationToken);

        if (old.Count == 0)
            return Release.AlreadyGone(resource);

        var removed = await docker.RunAsync(["buildx", "history", "rm", .. old], cancellationToken);

        return removed.IsSuccess ? Release.Done(resource, $"removed {old.Count} build record(s); the last day's stay") : Release.Refused(resource, removed.Failure);
    }

    private async Task<IReadOnlyList<string>> OldAsync(CancellationToken cancellationToken)
    {
        var listed = await docker.ReadAsync(["buildx", "history", "ls", "--format", "{{json .}}"], cancellationToken);
        var before = clock.GetUtcNow() - Kept;

        return
        [
            .. Docker.Lines(listed.Output)
                .Where(line => DateTimeOffset.TryParse(Docker.Text(line, "created_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var created) && created < before)
                .Select(line => Docker.Text(line, "ref").Split('/')[^1])
                .Where(reference => reference.Length > 0)
        ];
    }
}
