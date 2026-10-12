using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The name and version this server announces, read from the assembly rather than written twice.</summary>
public static class ThisAssembly
{
    /// <summary>The name a client knows this server by, the one its command carries.</summary>
    public static string Name { get; } = ServerIdentity.NameOf(typeof(ThisAssembly).Assembly);

    /// <summary>The version of the build that is running, without the commit a build may append.</summary>
    public static string Version { get; } = ServerIdentity.VersionOf(typeof(ThisAssembly).Assembly);
}
