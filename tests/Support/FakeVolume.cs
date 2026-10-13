using Snail.MCP.CleanUp.Application.Ports;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A volume whose free space the test sets.</summary>
internal sealed class FakeVolume : IVolume
{
    public long Free { get; set; } = 400_000_000_000;

    public long Total { get; set; } = 1_000_000_000_000;

    public bool IsReadable { get; set; } = true;

    public (long Free, long Total)? Measure() => IsReadable ? (Free, Total) : null;
}
