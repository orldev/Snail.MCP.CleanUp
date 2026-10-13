namespace Snail.MCP.CleanUp.Application;

/// <summary>How full the disk is, said before it is full rather than after.</summary>
/// <remarks>
/// A database on a full disk stops writing and, in a container, may go on looking healthy — measured on
/// 2026-10-10, when a stand's MongoDB fell over that way and nothing said so for hours. So every inventory
/// carries the free space, and a warning once it is low enough to matter.
/// </remarks>
public static class Disk
{
    /// <summary>Below this share of the disk free, the inventory warns.</summary>
    public const double LowShare = 0.10;

    /// <summary>Below this many bytes free, the inventory warns, however large the disk.</summary>
    public const long LowBytes = 20_000_000_000;

    /// <summary>The free space of the volume home lives on, with a warning when it is low.</summary>
    public static JsonObject Report()
    {
        var volume = new DriveInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return Report(volume.AvailableFreeSpace, volume.TotalSize);
    }

    /// <summary>The free space of a volume of the given size, with a warning when it is low.</summary>
    public static JsonObject Report(long free, long total)
    {
        var share = total > 0 ? (double)free / total : 0;

        return new JsonObject
        {
            ["free"] = Views.Size(free),
            ["freeShare"] = Math.Round(share, 3),
            ["warning"] = share < LowShare || free < LowBytes ? $"only {Views.Size(free)} free: plan the caches scope to see what can be given back" : null
        };
    }
}
