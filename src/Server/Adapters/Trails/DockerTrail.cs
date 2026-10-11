namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>Docker's containers, networks and volumes by id, so that one made without the owner label is still noticed.</summary>
public sealed class DockerTrail(IDockerEngine engine) : ITrail
{
    /// <inheritdoc/>
    public string Name => "docker";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        if (await engine.SnapshotAsync(cancellationToken) is not { } snapshot)
            return [];

        return
        [
            .. snapshot.Containers.Select(container => new Trace(Kinds.Container, container.Id, $"container {container.Name} ({container.Image})")),
            .. snapshot.Networks.Select(network => new Trace(Kinds.Network, network.Id, $"network {network.Name}")),
            .. snapshot.Volumes.Select(volume => new Trace(Kinds.Volume, volume.Name, $"volume {volume.Name}"))
        ];
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);
}
