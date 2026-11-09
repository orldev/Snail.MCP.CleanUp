using System.Collections;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Configuration;

public sealed class ServerConfigTests
{
    [Fact]
    public void DataDirectory_WhenNothingIsSet_IsTheSameForEverySession() =>
        Assert.Equal(
            Snail.Sdk.Mcp.Hosting.ServerFolders.DataDirectory("cleanup"),
            ServerConfigExtensions.Read(ServerConfigExtensions.Settings(new Dictionary<string, string>())).DataDirectory);

    [Fact]
    public void DataDirectory_FromThePrefixedVariable_IsTaken()
    {
        using var scratch = new Scratch();

        Assert.Equal(scratch.Path, ServerConfigExtensions.Read(Deployment.Settings(scratch)).DataDirectory);
    }

    [Fact]
    public void DataDirectory_Relative_StopsTheServer()
    {
        var settings = ServerConfigExtensions.Settings(new Dictionary<string, string>
        {
            [$"{EnvironmentSettings.Prefix}DATA_DIRECTORY"] = "relative/place"
        });

        var refused = Assert.Throws<Snail.Sdk.Mcp.Hosting.StartRefusedException>(() => ServerConfigExtensions.Read(settings));

        Assert.Equal(
            "SNAIL_MCP_CLEANUP_DATA_DIRECTORY (or DataDirectory in the settings file) is 'relative/place'; it is an absolute path.",
            refused.Message);
    }

    /// <summary>A path written the way a person writes it, and the way the manifest's defaults are, is the user's home.</summary>
    [Theory]
    [InlineData("DATA_DIRECTORY", nameof(ServerConfig.DataDirectory))]
    [InlineData("HOOK_DIRECTORY", nameof(ServerConfig.HookDirectory))]
    [InlineData("TRASH_DIRECTORY", nameof(ServerConfig.TrashDirectory))]
    [InlineData("SCRATCH_ROOT", nameof(ServerConfig.ScratchRoot))]
    [InlineData("SLEEP_OF_DIRECTORY", nameof(ServerConfig.SleepOfDirectory))]
    [InlineData("ANDROID_SDK", nameof(ServerConfig.AndroidSdk))]
    [InlineData("AVD_DIRECTORY", nameof(ServerConfig.AvdDirectory))]
    [InlineData("EDITOR_USER_DIRECTORY", nameof(ServerConfig.EditorUserDirectory))]
    [InlineData("CLAUDE_SETTINGS", nameof(ServerConfig.ClaudeSettings))]
    [InlineData("CLAUDE_PROJECTS", nameof(ServerConfig.ClaudeProjects))]
    public void Path_UnderTheHome_IsTheUsersHome(string variable, string property)
    {
        var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { [$"{EnvironmentSettings.Prefix}{variable}"] = "~/x" });

        var config = ServerConfigExtensions.Read(settings);
        var taken = typeof(ServerConfig).GetProperty(property)!.GetValue(config);

        Assert.Equal(Path.Combine(Snail.Sdk.Mcp.Hosting.ServerFolders.Home, "x"), taken);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Warning_OfNoSize_StopsTheServer(string gigabytes)
    {
        foreach (var variable in (string[])["SCRATCHPAD_WARNING_GIGABYTES", "DISK_FALL_WARNING_GIGABYTES", "DOCKER_CACHE_SWEEP_GIGABYTES", "SCRATCHPAD_ERASE_HOURS", "HISTORY_DAYS"])
        {
            var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { [$"{EnvironmentSettings.Prefix}{variable}"] = gigabytes });

            Assert.Throws<Snail.Sdk.Mcp.Hosting.StartRefusedException>(() => ServerConfigExtensions.Read(settings));
        }
    }

    /// <summary>The server's own sweep is turned off with 0 minutes; a negative interval is a typo, and stops the server.</summary>
    [Theory]
    [InlineData("0", true)]
    [InlineData("30", true)]
    [InlineData("-1", false)]
    public void SweepMinutes_ZeroTurnsTheSweepOff_ANegativeOneStopsTheServer(string minutes, bool isTaken)
    {
        var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { [$"{EnvironmentSettings.Prefix}SWEEP_MINUTES"] = minutes });

        if (isTaken)
            Assert.Equal(double.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture), ServerConfigExtensions.Read(settings).SweepMinutes);
        else
            Assert.Throws<Snail.Sdk.Mcp.Hosting.StartRefusedException>(() => ServerConfigExtensions.Read(settings));
    }

    [Fact]
    public void ScratchpadWarning_FromItsVariable_IsTaken() =>
        Assert.Equal(25, ServerConfigExtensions.Read(ServerConfigExtensions.Settings(new Dictionary<string, string> { [$"{EnvironmentSettings.Prefix}SCRATCHPAD_WARNING_GIGABYTES"] = "25" })).ScratchpadWarningGigabytes);

    [Fact]
    public void Variable_WithoutThePrefix_IsNotASetting()
    {
        var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { ["DATA_DIRECTORY"] = "/elsewhere" });

        Assert.NotEqual("/elsewhere", ServerConfigExtensions.Read(settings).DataDirectory);
    }

    [Fact]
    public void DataDirectory_FromTheSettingsFile_IsTaken()
    {
        using var scratch = new Scratch();
        var file = SettingsFile(scratch, new JsonObject { ["DataDirectory"] = scratch.File("FromFile") });

        Assert.Equal(scratch.File("FromFile"), ServerConfigExtensions.Read(ServerConfigExtensions.Settings(new Hashtable(), file)).DataDirectory);
    }

    [Fact]
    public void DataDirectory_UnderTheSnailSectionOfTheFile_IsTaken()
    {
        using var scratch = new Scratch();
        var file = SettingsFile(scratch, new JsonObject { [ServerConfig.SectionName] = new JsonObject { ["DataDirectory"] = scratch.File("Nested") } });

        Assert.Equal(scratch.File("Nested"), ServerConfigExtensions.Read(ServerConfigExtensions.Settings(new Hashtable(), file)).DataDirectory);
    }

    [Fact]
    public void Variable_OverTheSettingsFile_Wins()
    {
        using var scratch = new Scratch();
        var file = SettingsFile(scratch, new JsonObject { ["DataDirectory"] = scratch.File("FromFile") });
        var variables = new Hashtable { [$"{EnvironmentSettings.Prefix}DATA_DIRECTORY"] = scratch.File("FromVariable") };

        Assert.Equal(scratch.File("FromVariable"), ServerConfigExtensions.Read(ServerConfigExtensions.Settings(variables, file)).DataDirectory);
    }

    private static string SettingsFile(Scratch scratch, JsonObject settings)
    {
        var file = scratch.File("config.json");

        File.WriteAllText(file, settings.ToJsonString());

        return file;
    }
}
