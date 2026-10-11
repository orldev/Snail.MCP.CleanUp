namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>Docker's Engine, asked directly rather than through its command line.</summary>
public interface IDockerEngine
{
    /// <summary>Why the Engine cannot be asked right now; null when it can.</summary>
    Task<string?> UnreachableAsync(CancellationToken cancellationToken);

    /// <summary>Everything a census needs, read once and shared for a moment; null when the Engine cannot be asked.</summary>
    Task<DockerSnapshot?> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>The container with this id as it is now; null when there is none.</summary>
    Task<DockerContainer?> ContainerAsync(string id, CancellationToken cancellationToken);

    /// <summary>Removes an object: <c>containers</c> (forced), <c>networks</c>, <c>volumes</c> or <c>images</c>; how it went.</summary>
    Task<DockerAnswer> RemoveAsync(string collection, string id, CancellationToken cancellationToken);

    /// <summary>Prunes the build cache, or the images no tag points at.</summary>
    Task<DockerAnswer> PruneAsync(string what, CancellationToken cancellationToken);
}

/// <summary>How a request to the Engine ended: done, already gone, or refused with the Engine's words.</summary>
public sealed record DockerAnswer(bool IsDone, bool IsMissing, string Message);
