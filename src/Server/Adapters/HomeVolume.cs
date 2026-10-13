namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The volume a directory lives on — home's, unless told another — as the system reports it.</summary>
/// <param name="directory">A directory on the volume.</param>
public sealed class HomeVolume(string directory) : IVolume
{
    /// <summary>The volume home lives on.</summary>
    public HomeVolume()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <inheritdoc/>
    public (long Free, long Total)? Measure()
    {
        try
        {
            var volume = new DriveInfo(directory);

            return (volume.AvailableFreeSpace, volume.TotalSize);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
