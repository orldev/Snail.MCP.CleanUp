using System.Reflection;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Tools;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>The tools the assembly declares, found by reflection rather than listed.</summary>
/// <remarks>A tool type added and forgotten is exactly what a guard written as a list fails to catch.</remarks>
internal static class Catalog
{
    public static IReadOnlyList<MethodInfo> Tools { get; } =
    [
        .. typeof(ToolResponse).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    public static IReadOnlyList<string> Names { get; } =
        [.. Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!).Order(StringComparer.Ordinal)];

    public static IEnumerable<string> Named(Func<McpServerToolAttribute, bool> wanted) =>
        Tools.Select(tool => tool.GetCustomAttribute<McpServerToolAttribute>()!)
            .Where(wanted)
            .Select(tool => tool.Name!)
            .Order(StringComparer.Ordinal);
}
