using System.Reflection;

namespace Snail.MCP.CleanUp.Configuration;

/// <summary>The name and version this server announces, read from the assembly rather than written twice.</summary>
public static class ThisAssembly
{
    /// <summary>The name a client and the registry know this server by.</summary>
    public const string Name = "snail-mcp-cleanup";

    /// <summary>The version of the build that is running, without the commit a build may append.</summary>
    public static string Version { get; } =
        typeof(ThisAssembly).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0] ?? "0.0.0";
}
