namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The space something takes, and whether that is all of it or only what was counted before the count stopped.</summary>
/// <param name="Bytes">The bytes counted.</param>
/// <param name="IsAtLeast">Whether the count stopped short, so that the size is at least <paramref name="Bytes"/> and maybe much more.</param>
public readonly record struct Measured(long Bytes, bool IsAtLeast);
