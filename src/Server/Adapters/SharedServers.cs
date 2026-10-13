using System.Collections.Frozen;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The servers every build and every editor of the machine shares, whichever session happened to start them.</summary>
/// <remarks>
/// A build leaves its servers running to be reused: MSBuild's worker nodes (<c>MSBuild.dll /nodemode:… /nodeReuse:true</c>),
/// the Roslyn compiler server (<c>VBCSCompiler</c>) and the Razor one (<c>rzc</c>) — what <c>dotnet build-server shutdown</c>
/// stops. An editor does the same with its language servers: OmniSharp, Roslyn's and Razor's language servers and their build
/// hosts, Rider's backend and workers. The one that starts such a server hands it its environment and is its parent for a
/// moment, yet the next build of any window and the editor reuse it: on 2026-10-13 four MSBuild nodes and a VBCSCompiler
/// carried this session's id an hour after its builds. So neither the environment nor ancestry makes one anybody's: it is
/// named, and never planned for removal on ownership.
/// </remarks>
public static class SharedServers
{
    /// <summary>Why a shared server is left, as a plan says it.</summary>
    public const string Why = "a server builds and editors of every window share and reuse: whichever session started it, it is nobody's to end";

    /// <summary>The programs and assemblies that are such servers, matched by their name without an extension.</summary>
    private static readonly FrozenSet<string> Names = FrozenSet.Create(
        StringComparer.Ordinal,
        "VBCSCompiler",
        "rzc",
        "OmniSharp",
        "Microsoft.CodeAnalysis.LanguageServer",
        "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost",
        "rzls",
        "Microsoft.VisualStudio.Code.ServiceHost",
        "Microsoft.ServiceHub.Controller",
        "Rider.Backend",
        "JetBrains.ReSharper.Roslyn.Worker",
        "JetBrains.Debugger.Worker");

    /// <summary>Whether a process is one of these servers, by its program, the assembly a runtime was given, or MSBuild's node switches.</summary>
    public static bool Is(RunningProcess process)
    {
        if (Names.Contains(process.Program) || IsMSBuildNode(process.Command))
            return true;

        foreach (var word in process.Command.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries).Take(3))
        {
            var name = Path.GetFileName(word.AsSpan());

            if (name.EndsWith(".dll", StringComparison.Ordinal) && Names.Contains(name[..^4].ToString()))
                return true;
        }

        return false;
    }

    /// <summary>MSBuild run as a node of another build — a worker or the build server — rather than as a build of its own.</summary>
    private static bool IsMSBuildNode(string command) =>
        command.Contains("MSBuild.dll", StringComparison.Ordinal)
        && (command.Contains("/nodemode:", StringComparison.OrdinalIgnoreCase) || command.Contains("-nodemode:", StringComparison.OrdinalIgnoreCase) || command.Contains("nodeReuse", StringComparison.OrdinalIgnoreCase));
}
