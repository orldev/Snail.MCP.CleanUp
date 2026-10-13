namespace Snail.MCP.CleanUp.Domain;

/// <summary>The free space of the volume home lives on, as one census found it.</summary>
/// <param name="At">When.</param>
/// <param name="Free">The bytes free.</param>
/// <param name="Total">The bytes of the whole volume.</param>
public sealed record DiskSample(DateTimeOffset At, long Free, long Total);

/// <summary>The size of one thing that grows on the disk, as one census found it.</summary>
/// <param name="At">When.</param>
/// <param name="ResourceId">The <see cref="Resource.Id"/> it is about.</param>
/// <param name="Title">What a person reads to recognise it.</param>
/// <param name="Bytes">Its size.</param>
public sealed record SizeSample(DateTimeOffset At, string ResourceId, string Title, long Bytes);
