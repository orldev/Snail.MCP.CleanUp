using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Configuration;

public sealed class MacPlacesTests
{
    /// <summary>This server's account and the published programs are never a trail, under the s3l names and the ones of before.</summary>
    [Theory]
    [InlineData(".s3l-mcp-cleanup/ledger.db")]
    [InlineData(".snail-mcp-cleanup/ledger.db")]
    [InlineData(".s3l-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp")]
    [InlineData(".snail-mcp/Snail.MCP.CleanUp/current/Snail.MCP.CleanUp")]
    public void ShippedRules_IgnoreTheOwnAccountAndThePrograms_UnderEitherName(string place)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var trace = new Trace("file", $"{home}/{place}", "x");

        Assert.Contains(MacPlaces.Shipped, rule => rule.Action == RuleAction.Ignore && rule.Matches(trace));
    }
}
