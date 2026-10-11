using System.Globalization;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Where a removed file or directory goes: the Trash when it cannot be made again, nowhere when it rebuilds itself.</summary>
/// <remarks>
/// The Trash is the undo a person already knows. It does not free space, though, and a cache of gigabytes
/// moved there is a cache of gigabytes still on the disk — so what rebuilds itself is erased, and only what
/// would be lost goes where it can be taken back from.
/// </remarks>
public sealed class Disposal(ServerConfig config, TimeProvider clock)
{
    /// <summary>Removes the file or directory at the path the way its nature calls for.</summary>
    public Release Dispose(Resource resource, string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path) && !IsLink(path))
            return Release.AlreadyGone(resource);

        try
        {
            return resource.IsRegenerable ? Erased(resource, path) : Trashed(resource, path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return Release.Refused(resource, failure.Message);
        }
    }

    private static Release Erased(Resource resource, string path)
    {
        if (IsLink(path) || File.Exists(path))
            File.Delete(path);
        else
            Directory.Delete(path, recursive: true);

        return Release.Done(resource, "erased: it rebuilds itself");
    }

    private Release Trashed(Resource resource, string path)
    {
        Directory.CreateDirectory(config.TrashDirectory);

        var destination = FreeName(Path.Combine(config.TrashDirectory, Path.GetFileName(Path.TrimEndingDirectorySeparator(path))));

        if (Directory.Exists(path) && !IsLink(path))
            Directory.Move(path, destination);
        else
            File.Move(path, destination);

        return Release.Done(resource, $"moved to the Trash as {Path.GetFileName(destination)}");
    }

    private string FreeName(string wanted)
    {
        if (!File.Exists(wanted) && !Directory.Exists(wanted))
            return wanted;

        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

        return $"{wanted} {stamp}";
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;
}
