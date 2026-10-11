using System.Text.Json;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>VS Code's state of folders that no longer exist: what every worktree an agent opened and deleted leaves behind.</summary>
/// <remarks>
/// VS Code keeps a folder of state for every workspace it ever opened and never forgets one. An agent that
/// works in a worktree, opens it and removes it leaves that state with nothing to belong to. It holds nothing
/// anyone can use again, so it is a cache in effect — removed only when caches are asked for, and not while
/// it was written in the last hour, which is what a window still open on a deleted folder looks like.
/// </remarks>
public sealed class EditorKind(ServerConfig config, Disposal disposal, TimeProvider clock) : IResourceKind
{
    /// <summary>How recently written state counts as in use.</summary>
    public static TimeSpan InUse { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc/>
    public string Name => Kinds.Editor;

    private string Storage => Path.Combine(config.EditorUserDirectory, "workspaceStorage");

    /// <inheritdoc/>
    public Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Storage))
            return Task.FromResult(new Observation(Name, [], []));

        List<Resource> orphaned = [.. Directory.EnumerateDirectories(Storage).Select(Described).OfType<Resource>()];

        return Task.FromResult(new Observation(Name, orphaned, []));
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
        Task.FromResult(Folder(resource.Key) is { } folder && Exists(folder)
            ? Release.NotTheSame(resource, $"{folder} exists again")
            : disposal.Dispose(resource, resource.Key));

    private Resource? Described(string directory)
    {
        var folder = Folder(directory);

        if (folder is null || Exists(folder))
            return null;

        var written = Directory.EnumerateFiles(directory).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory)).Max();

        return new Resource
        {
            Kind = Name,
            Key = directory,
            Title = $"VS Code state of {folder}, which no longer exists",
            IsShared = true,
            IsRegenerable = true,
            Bytes = Sizes.Of(directory),
            Blocker = clock.GetUtcNow().UtcDateTime - written < InUse ? "written in the last hour: a window may still be open on it" : null
        };
    }

    /// <summary>The local folder or workspace file the state belongs to; null for a remote one or one that cannot be read.</summary>
    internal static string? Folder(string directory)
    {
        var description = Path.Combine(directory, "workspace.json");

        if (!File.Exists(description))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(description));
            var root = document.RootElement;
            var address = root.TryGetProperty("folder", out var folder) ? folder.GetString() : root.TryGetProperty("workspace", out var workspace) ? workspace.GetString() : null;

            return address is not null && address.StartsWith("file://", StringComparison.Ordinal) ? new Uri(address).LocalPath : null;
        }
        catch (Exception unreadable) when (unreadable is JsonException or IOException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
}
