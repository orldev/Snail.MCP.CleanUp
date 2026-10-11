using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tools;

/// <summary>Every tool of this server as an entry: what it is called, what it takes, and what each argument means.</summary>
/// <remarks>
/// Read from the tools themselves rather than written a second time beside them, because a catalog kept by
/// hand is wrong by the second release. It answers a question asked in words the tool list is not written
/// in — «free port 3000», «my simulator is stuck» — in one call rather than by trying a tool and reading the
/// refusal. Scored the same way as the other servers of the fleet.
/// </remarks>
public sealed class ToolIndex
{
    /// <summary>Every tool of this assembly: this server's own first, then the ones the fleet shares.</summary>
    public static readonly IReadOnlyList<ToolEntry> All = Read();

    private const string Prefix = "cleanup_";

    /// <summary>The words a caller reaches for that the tool is not named with; read only when someone searches.</summary>
    private static readonly Dictionary<string, string> Meanings = new(StringComparer.Ordinal)
    {
        ["cleanup_inventory"] = "list show what left leftovers garbage junk running orphans disk space full port ports occupied status",
        ["cleanup_plan"] = "clean cleanup tidy remove delete free decide preview dry run what would",
        ["cleanup_apply"] = "execute carry confirm go ahead remove delete kill stop do it",
        ["cleanup_release"] = "release stop kill end delete shutdown free port simulator emulator one",
        ["cleanup_keep"] = "keep pin protect spare exclude leave alone ignore preserve",
        ["cleanup_learn"] = "learn rule teach new kind discovery unknown pattern classify",
        ["cleanup_run"] = "start launch run background server dev watcher worker daemon npm serve",
        ["cleanup_simulator_acquire"] = "ios iphone simulator device xcode simctl test boot own clone",
        ["cleanup_emulator_acquire"] = "android emulator avd device adb phone test boot own",
        ["cleanup_diagnose"] = "health status broken why session which tools available docker adb simctl",
        ["find_tool"] = "search discover name parameters arguments how which"
    };

    /// <summary>The tools that answer a question, best first.</summary>
    public IReadOnlyList<ToolEntry> Find(string? task, int limit) =>
    [
        .. All
            .Select(entry => (Entry: entry, Score: Vocabulary.Score(task ?? string.Empty, entry.Name, Meanings.GetValueOrDefault(entry.Name), entry.Description)))
            .Where(scored => scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Entry.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, All.Count))
            .Select(scored => scored.Entry)
    ];

    /// <summary>The answer <c>find_tool</c> gives: what matched, or the whole catalog when nothing did.</summary>
    public JsonObject Answer(string? task, int limit)
    {
        var found = Find(task, limit);

        return new JsonObject
        {
            ["task"] = task,
            ["isMatched"] = found.Count > 0,
            ["note"] = found.Count > 0 ? null : "nothing in this server's words matches; these are all of its tools",
            ["tools"] = new JsonArray([.. (found.Count > 0 ? found : All).Select(Describe)])
        };
    }

    /// <summary>The tools the index knows other words for; a guard test keeps these the tools that exist.</summary>
    internal static IEnumerable<string> Meant() => Meanings.Keys;

    private static JsonNode Describe(ToolEntry entry) => new JsonObject
    {
        ["name"] = entry.Name,
        ["description"] = entry.Description,
        ["parameters"] = new JsonArray([.. entry.Parameters.Select(parameter => (JsonNode)new JsonObject
        {
            ["name"] = parameter.Name,
            ["type"] = parameter.Type,
            ["isRequired"] = parameter.IsRequired,
            ["description"] = parameter.Description
        })])
    };

    private static IReadOnlyList<ToolEntry> Read()
    {
        var entries = typeof(ToolIndex).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(Entry);

        return [.. entries.OrderBy(entry => entry.Name.StartsWith(Prefix, StringComparison.Ordinal) ? 0 : 1).ThenBy(entry => entry.Name, StringComparer.Ordinal)];
    }

    private static ToolEntry Entry(MethodInfo tool) => new(
        tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!,
        tool.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty,
        [.. tool.GetParameters().Where(parameter => parameter.ParameterType != typeof(CancellationToken)).Select(Argument)]);

    private static ToolArgument Argument(ParameterInfo parameter) => new(
        parameter.Name!,
        Shape(parameter.ParameterType),
        !parameter.HasDefaultValue,
        parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty);

    private static string Shape(Type type)
    {
        var given = Nullable.GetUnderlyingType(type) ?? type;

        return given switch
        {
            _ when given == typeof(string) => "string",
            _ when given == typeof(bool) => "boolean",
            _ when given == typeof(int) => "integer",
            _ when given == typeof(string[]) => "string[]",
            _ => "object"
        };
    }
}

/// <summary>One tool as the index knows it.</summary>
public sealed record ToolEntry(string Name, string Description, IReadOnlyList<ToolArgument> Parameters);

/// <summary>One argument of a tool, with the words the model reads before filling it.</summary>
public sealed record ToolArgument(string Name, string Type, bool IsRequired, string Description);
