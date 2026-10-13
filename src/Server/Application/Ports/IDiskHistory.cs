namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>How the free space and the largest growers moved, census by census, over the last days.</summary>
public interface IDiskHistory
{
    /// <summary>Writes down one census: the free space, and the sizes of the things that grow.</summary>
    Task RecordAsync(DiskSample disk, IReadOnlyList<SizeSample> sizes, CancellationToken cancellationToken);

    /// <summary>The free space written down since a moment, oldest first.</summary>
    Task<IReadOnlyList<DiskSample>> FreeSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The sizes written down since a moment, oldest first.</summary>
    Task<IReadOnlyList<SizeSample>> SizesSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>The volume home lives on.</summary>
public interface IVolume
{
    /// <summary>Its free and total bytes; null when it cannot be read.</summary>
    (long Free, long Total)? Measure();
}
