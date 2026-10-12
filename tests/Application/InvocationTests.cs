using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tests.Application;

public sealed class InvocationTests : IDisposable
{
    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    /// <summary>A hook that named the publication's own folder would break when a later publication removes it.</summary>
    [Fact]
    public void Program_RunFromThePublicationCurrentPointsAt_IsNamedThroughCurrent()
    {
        var publication = Directory.CreateDirectory(_scratch.File("2026-10-12-0413.988248c")).FullName;
        Directory.CreateSymbolicLink(_scratch.File("current"), "2026-10-12-0413.988248c");

        Assert.Equal(_scratch.File("current/Snail.MCP.CleanUp"), Invocation.Stable(Path.Combine(publication, "Snail.MCP.CleanUp")));
    }

    [Fact]
    public void Program_RunFromAnOlderPublication_OrFromABuild_KeepsItsOwnPath()
    {
        var older = Directory.CreateDirectory(_scratch.File("2026-10-11-0900.1a2b3c4")).FullName;
        Directory.CreateDirectory(_scratch.File("2026-10-12-0413.988248c"));
        Directory.CreateSymbolicLink(_scratch.File("current"), "2026-10-12-0413.988248c");

        Assert.Equal(Path.Combine(older, "Snail.MCP.CleanUp"), Invocation.Stable(Path.Combine(older, "Snail.MCP.CleanUp")));
        Assert.Equal("/repo/src/Server/bin/Debug/net10.0/Snail.MCP.CleanUp.dll", Invocation.Stable("/repo/src/Server/bin/Debug/net10.0/Snail.MCP.CleanUp.dll"));
    }

    /// <summary>A native tool package is unpacked into a folder per version, and its command links to the program inside.</summary>
    [Fact]
    public void Program_RunFromADotnetToolStore_IsNamedByItsCommand()
    {
        var inside = Directory.CreateDirectory(_scratch.File(".store/snail.mcp.cleanup/0.1.0/snail.mcp.cleanup.osx-arm64/0.1.0/tools/any/osx-arm64")).FullName;
        var program = Path.Combine(inside, "Snail.MCP.CleanUp");
        File.WriteAllText(program, string.Empty);
        File.CreateSymbolicLink(_scratch.File("snail-mcp-cleanup"), ".store/snail.mcp.cleanup/0.1.0/snail.mcp.cleanup.osx-arm64/0.1.0/tools/any/osx-arm64/Snail.MCP.CleanUp");

        Assert.Equal(_scratch.File("snail-mcp-cleanup"), Invocation.Stable(program));
    }
}
