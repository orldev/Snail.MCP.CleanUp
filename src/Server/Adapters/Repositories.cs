using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The repositories the machine already names, never the whole disk: the directories sessions work in, and the folders VS Code keeps state for.</summary>
/// <remarks>A directory counts only if it holds <c>.git</c>; the home folder and Claude Code's scratch root never do.</remarks>
public sealed class Repositories(ServerConfig config)
{
    /// <summary>The directories the holders work in, each once and in order: what a search of the repositories is keyed by.</summary>
    public static IReadOnlyList<string> Worked(IReadOnlyList<Holder> holders) =>
        [.. holders.Select(holder => holder.Directory).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The repositories among the directories sessions work in and the folders the editor opened.</summary>
    public IReadOnlyList<string> Named(IReadOnlyList<string> worked)
    {
        var home = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var scratch = Path.Combine(config.ScratchRoot, "claude-");

        return
        [
            .. worked
                .Concat(Edited())
                .Select(Path.TrimEndingDirectorySeparator)
                .Distinct(StringComparer.Ordinal)
                .Where(directory => directory.Length > 1
                    && !string.Equals(directory, home, StringComparison.Ordinal)
                    && !directory.StartsWith(scratch, StringComparison.Ordinal)
                    && (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"))))
        ];
    }

    private IEnumerable<string> Edited()
    {
        var storage = Path.Combine(config.EditorUserDirectory, "workspaceStorage");

        if (!Directory.Exists(storage))
            return [];

        try
        {
            return [.. Directory.EnumerateDirectories(storage).Select(EditorKind.Folder).OfType<string>()];
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
