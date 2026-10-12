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

    [Fact]
    public void Variable_WithoutThePrefix_IsNotASetting()
    {
        var settings = ServerConfigExtensions.Settings(new Dictionary<string, string> { ["DATA_DIRECTORY"] = "/elsewhere" });

        Assert.NotEqual("/elsewhere", ServerConfigExtensions.Read(settings).DataDirectory);
    }
}
