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

        Assert.Throws<InvalidOperationException>(() => ServerConfigExtensions.Read(settings));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Warning_OfNoSize_StopsTheServer(string gigabytes)
    {
        foreach (var variable in (string[])["SCRATCHPAD_WARNING_GIGABYTES", "DISK_FALL_WARNING_GIGABYTES"])
        {
            var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { [$"{EnvironmentSettings.Prefix}{variable}"] = gigabytes });

            Assert.Throws<InvalidOperationException>(() => ServerConfigExtensions.Read(settings));
        }
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
