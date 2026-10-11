namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Docker containers: owned when they carry the owner label, never otherwise.</summary>
/// <remarks>
/// Docker objects say nothing about who made them unless they were told to. A container started with
/// <c>--label snail.owner=…</c> is proof; one without it is named and left alone, however recently it
/// appeared — and the trail notices it as something new.
/// </remarks>
public sealed class ContainerKind(Docker docker) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Container;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var listed = await docker.RunAsync(["ps", "-a", "--no-trunc", "--format", "{{json .}}"], cancellationToken);

        return new Observation(Name, [.. Docker.Lines(listed.Output).Select(Described)], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var listed = await docker.RunAsync(["ps", "-a", "--no-trunc", "--filter", $"id={resource.Key}", "--format", "{{json .}}"], cancellationToken);
        if (Docker.Lines(listed.Output) is not [var present, ..])
            return Release.AlreadyGone(resource);

        if (Docker.OwnerOf(Docker.Labels(Docker.Text(present, "Labels"))) != resource.Owner && resource.Evidence == Evidence.Label)
            return Release.NotTheSame(resource, "its owner label changed since the plan was made");

        var removed = await docker.RunAsync(["rm", "-f", resource.Key], cancellationToken);

        return removed.IsSuccess ? Release.Done(resource, $"removed container {Docker.Text(present, "Names")}") : Release.Refused(resource, removed.Failure);
    }

    private Resource Described(System.Text.Json.JsonElement line)
    {
        var labels = Docker.Labels(Docker.Text(line, "Labels"));
        var owner = Docker.OwnerOf(labels);

        return new Resource
        {
            Kind = Name,
            Key = Docker.Text(line, "ID"),
            Title = $"container {Docker.Text(line, "Names")} ({Docker.Text(line, "Image")}, {Docker.Text(line, "State")})",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            Facts = new Dictionary<string, string>
            {
                ["name"] = Docker.Text(line, "Names"),
                ["networks"] = Docker.Text(line, "Networks"),
                ["mounts"] = Docker.Text(line, "Mounts")
            }
        };
    }
}

/// <summary>Docker networks and volumes: owned by the label, released after every container that uses them.</summary>
public sealed class NetworkKind(Docker docker) : IResourceKind
{
    private static readonly HashSet<string> Builtin = new(StringComparer.Ordinal) { "bridge", "host", "none" };

    /// <inheritdoc/>
    public string Name => Kinds.Network;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var networks = Docker.Lines((await docker.RunAsync(["network", "ls", "--no-trunc", "--format", "{{json .}}"], cancellationToken)).Output);
        var containers = Docker.Lines((await docker.RunAsync(["ps", "-a", "--no-trunc", "--format", "{{json .}}"], cancellationToken)).Output);

        return new Observation(Name, [.. networks.Where(line => !Builtin.Contains(Docker.Text(line, "Name"))).Select(line => Described(line, containers))], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await docker.RunAsync(["network", "rm", resource.Key], cancellationToken);

        if (removed.IsSuccess)
            return Release.Done(resource, $"removed network {resource.Facts["name"]}");

        return removed.Error.Contains("not found", StringComparison.OrdinalIgnoreCase) ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Failure);
    }

    private Resource Described(System.Text.Json.JsonElement line, IReadOnlyList<System.Text.Json.JsonElement> containers)
    {
        var name = Docker.Text(line, "Name");
        var owner = Docker.OwnerOf(Docker.Labels(Docker.Text(line, "Labels")));
        var users = containers.Where(container => Docker.Text(container, "Networks").Split(',').Contains(name, StringComparer.Ordinal));

        return new Resource
        {
            Kind = Name,
            Key = Docker.Text(line, "ID"),
            Title = $"network {name}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            After = [.. users.Select(container => $"{Kinds.Container}:{Docker.Text(container, "ID")}")],
            Facts = new Dictionary<string, string> { ["name"] = name }
        };
    }
}

/// <summary>Docker volumes: the one Docker object that holds work, so it goes only on its label and only after its containers.</summary>
public sealed class VolumeKind(Docker docker) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Volume;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var volumes = Docker.Lines((await docker.RunAsync(["volume", "ls", "--format", "{{json .}}"], cancellationToken)).Output);
        var containers = Docker.Lines((await docker.RunAsync(["ps", "-a", "--no-trunc", "--format", "{{json .}}"], cancellationToken)).Output);

        return new Observation(Name, [.. volumes.Select(line => Described(line, containers))], []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await docker.RunAsync(["volume", "rm", resource.Key], cancellationToken);

        if (removed.IsSuccess)
            return Release.Done(resource, $"removed volume {resource.Key}");

        return removed.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase) ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Failure);
    }

    private Resource Described(System.Text.Json.JsonElement line, IReadOnlyList<System.Text.Json.JsonElement> containers)
    {
        var name = Docker.Text(line, "Name");
        var owner = Docker.OwnerOf(Docker.Labels(Docker.Text(line, "Labels")));
        var users = containers.Where(container => Docker.Text(container, "Mounts").Split(',').Contains(name, StringComparer.Ordinal));

        return new Resource
        {
            Kind = Name,
            Key = name,
            Title = $"volume {name}",
            Owner = owner,
            Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
            After = [.. users.Select(container => $"{Kinds.Container}:{Docker.Text(container, "ID")}")]
        };
    }
}

/// <summary>Docker images built with the owner label, and the build cache every build shares.</summary>
/// <remarks>
/// The build cache belongs to the machine, not to an agent, and rebuilds itself: it is a cache in every
/// sense, removed only when caches are asked for by name. It is also where the disk goes — a stand whose
/// disk filled stopped its database without a word — so its size is always in the inventory.
/// </remarks>
public sealed class ImageKind(Docker docker) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.Image;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var labelled = Docker.Lines((await docker.RunAsync(["image", "ls", "--no-trunc", "--filter", $"label={Docker.OwnerLabel}", "--format", "{{json .}}"], cancellationToken)).Output);
        var found = new List<Resource>();

        foreach (var line in labelled)
        {
            var id = Docker.Text(line, "ID");
            var owner = await OwnerAsync(id, cancellationToken);

            found.Add(new Resource
            {
                Kind = Name,
                Key = id,
                Title = $"image {Docker.Text(line, "Repository")}:{Docker.Text(line, "Tag")}",
                Owner = owner,
                Evidence = owner.IsKnown ? Evidence.Label : Evidence.None,
                Bytes = Docker.Bytes(Docker.Text(line, "Size"))
            });
        }

        return new Observation(Name, found, []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var removed = await docker.RunAsync(["image", "rm", resource.Key], cancellationToken);

        if (removed.IsSuccess)
            return Release.Done(resource, $"removed {resource.Title}");

        return removed.Error.Contains("No such image", StringComparison.OrdinalIgnoreCase) ? Release.AlreadyGone(resource) : Release.Refused(resource, removed.Failure);
    }

    private async Task<Owner> OwnerAsync(string id, CancellationToken cancellationToken)
    {
        var inspected = await docker.RunAsync(["image", "inspect", "--format", $"{{{{index .Config.Labels \"{Docker.OwnerLabel}\"}}}}", id], cancellationToken);

        return inspected.IsSuccess ? Owner.Parse(inspected.Output.Trim()) : Owner.Nobody;
    }
}

/// <summary>Docker's build cache, as one shared resource the size of what it could give back.</summary>
public sealed class BuildCacheKind(Docker docker) : IResourceKind
{
    /// <inheritdoc/>
    public string Name => Kinds.BuildCache;

    /// <inheritdoc/>
    public async Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is { } reason)
            return Observation.Unseen(Name, reason);

        var usage = Docker.Lines((await docker.RunAsync(["system", "df", "--format", "{{json .}}"], cancellationToken)).Output);
        var cache = usage.FirstOrDefault(line => Docker.Text(line, "Type") == "Build Cache");
        var bytes = cache.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : Docker.Bytes(Docker.Text(cache, "Reclaimable"));

        if (bytes is not > 0)
            return new Observation(Name, [], []);

        return new Observation(
            Name,
            [
                new Resource
                {
                    Kind = Name,
                    Key = "docker",
                    Title = $"Docker build cache, {Docker.Text(cache, "Reclaimable")} reclaimable",
                    IsShared = true,
                    IsRegenerable = true,
                    Bytes = bytes
                }
            ],
            []);
    }

    /// <inheritdoc/>
    public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var pruned = await docker.RunAsync(["builder", "prune", "-f"], cancellationToken);

        return pruned.IsSuccess ? Release.Done(resource, "pruned the build cache; the next build fills what it needs") : Release.Refused(resource, pruned.Failure);
    }
}
