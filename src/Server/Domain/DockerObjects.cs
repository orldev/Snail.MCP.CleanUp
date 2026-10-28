namespace Snail.MCP.CleanUp.Domain;

/// <summary>A Docker container as the Engine lists it.</summary>
public sealed record DockerContainer(string Id, string Name, string Image, string State, IReadOnlyDictionary<string, string> Labels, IReadOnlyList<string> Networks, IReadOnlyList<string> Volumes);

/// <summary>A Docker network.</summary>
public sealed record DockerNetwork(string Id, string Name, IReadOnlyDictionary<string, string> Labels);

/// <summary>A Docker volume.</summary>
public sealed record DockerVolume(string Name, IReadOnlyDictionary<string, string> Labels);

/// <summary>A Docker image.</summary>
public sealed record DockerImage(string Id, IReadOnlyList<string> Tags, long Size, IReadOnlyDictionary<string, string> Labels);

/// <summary>Everything a census needs of Docker, read at one moment.</summary>
/// <param name="Containers">Every container, running or not.</param>
/// <param name="Networks">Every network.</param>
/// <param name="Volumes">Every volume.</param>
/// <param name="Labelled">The images that carry the owner label.</param>
/// <param name="Dangling">The images no tag points at.</param>
/// <param name="BuildCacheReclaimable">Bytes of build cache a prune gives back: no build uses it, and none used it in the last day.</param>
public sealed record DockerSnapshot(
    IReadOnlyList<DockerContainer> Containers,
    IReadOnlyList<DockerNetwork> Networks,
    IReadOnlyList<DockerVolume> Volumes,
    IReadOnlyList<DockerImage> Labelled,
    IReadOnlyList<DockerImage> Dangling,
    long BuildCacheReclaimable);
