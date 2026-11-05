using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Extensions;
using Snail.MCP.CleanUp.Tools;
using Snail.Sdk.Mcp.Hosting.Discovery;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>The tools the assembly declares, found by reflection rather than listed.</summary>
/// <remarks>A tool type added and forgotten is exactly what a guard written as a list fails to catch.</remarks>
internal static class Catalog
{
    public static IReadOnlyList<MethodInfo> Tools { get; } =
    [
        .. typeof(AccountTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    public static IReadOnlyList<string> Names { get; } =
        [.. Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal)];

    /// <summary>Every tool a client is sent, as the server's registration builds them, the ones Snail.Sdk.Mcp adds among them.</summary>
    /// <remarks>Read from the registration rather than written beside it: a list of the SDK's tools here drifts the day the registration adds one.</remarks>
    public static IReadOnlyList<ToolEntry> Entries { get; } = ToolCatalog.Of(new ServiceCollection().AddMcpTransport().BuildServiceProvider());

    /// <summary>The names of every tool a client is sent.</summary>
    public static IReadOnlyList<string> Listed { get; } = [.. Entries.Select(entry => entry.Name).Order(StringComparer.Ordinal)];

    /// <summary>The tools Snail.Sdk.Mcp registers for the server: no method of its classes, and named alike in every server of the fleet.</summary>
    public static IReadOnlyList<string> Shared { get; } = [.. Listed.Except(Names, StringComparer.Ordinal)];

    public static IEnumerable<string> Named(Func<McpServerToolAttribute, bool> wanted) =>
        Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!)
            .Where(wanted)
            .Select(tool => tool.Name!)
            .Order(StringComparer.Ordinal);
}
