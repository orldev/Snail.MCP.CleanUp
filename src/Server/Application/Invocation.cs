namespace Snail.MCP.CleanUp.Application;

/// <summary>How this program is started again: as the tool it was installed as, or as a dll run by dotnet.</summary>
/// <remarks>
/// It is started again the way it was started, and a dll run by dotnet is not traded for the native launcher next to
/// it. Measured on 2026-10-12, a hook costs 67 ms either way once it has run, but the launcher is signed only ad hoc, so
/// macOS looks into every new copy at its first start: 0.4 s for a copy, 3 s after a build — paid by the first hook
/// after every update, and gaining nothing after.
/// </remarks>
public static class Invocation
{
    /// <summary>The program and the arguments that start this server, before any verb.</summary>
    public static (string Program, IReadOnlyList<string> Arguments) Self()
    {
        var process = Environment.ProcessPath ?? "dotnet";
        var assembly = Path.Combine(AppContext.BaseDirectory, $"{typeof(Invocation).Assembly.GetName().Name}.dll");

        return Path.GetFileNameWithoutExtension(process) == "dotnet" ? (process, [Snail.Sdk.Mcp.Hosting.Invocation.Stable(assembly)]) : (Snail.Sdk.Mcp.Hosting.Invocation.Stable(process), []);
    }

    /// <summary>The same, as one command line for a hook entry.</summary>
    public static string CommandLine(string verb)
    {
        var (program, arguments) = Self();

        return Quoted([program, .. arguments, verb]);
    }

    /// <summary>The command line of a hook run by the native forwarder: the socket first, then this program as it runs when no server answers.</summary>
    public static string Forwarded(string forwarder, string socket, string verb)
    {
        var (program, arguments) = Self();

        return Quoted([forwarder, socket, program, .. arguments, verb]);
    }

    private static string Quoted(IEnumerable<string> words) =>
        string.Join(' ', words.Select(word => word.Contains(' ', StringComparison.Ordinal) ? $"\"{word}\"" : word));
}
