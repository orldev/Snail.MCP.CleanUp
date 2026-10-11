namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>Docker's containers, networks and volumes by id, so that one made without the owner label is still noticed.</summary>
public sealed class DockerTrail(Docker docker) : ITrail
{
    /// <inheritdoc/>
    public string Name => "docker";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        if (await docker.UnreachableAsync(cancellationToken) is not null)
            return [];

        var containers = Docker.Lines((await docker.RunAsync(["ps", "-a", "--no-trunc", "--format", "{{json .}}"], cancellationToken)).Output);
        var networks = Docker.Lines((await docker.RunAsync(["network", "ls", "--no-trunc", "--format", "{{json .}}"], cancellationToken)).Output);
        var volumes = Docker.Lines((await docker.RunAsync(["volume", "ls", "--format", "{{json .}}"], cancellationToken)).Output);

        return
        [
            .. containers.Select(line => new Trace(Kinds.Container, Docker.Text(line, "ID"), $"container {Docker.Text(line, "Names")} ({Docker.Text(line, "Image")})")),
            .. networks.Select(line => new Trace(Kinds.Network, Docker.Text(line, "ID"), $"network {Docker.Text(line, "Name")}")),
            .. volumes.Select(line => new Trace(Kinds.Volume, Docker.Text(line, "Name"), $"volume {Docker.Text(line, "Name")}"))
        ];
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);
}
