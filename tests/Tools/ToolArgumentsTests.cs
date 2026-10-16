using Snail.MCP.CleanUp.Tools;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Tools;

/// <summary>
/// Scope is <c>mine = 0, orphans = 1, caches = 2, everything = 3</c>, and <c>Enum.TryParse</c> ORs names joined by commas:
/// "orphans,caches" was read as everything, the asker's own running programs among the removals (2026-10-16).
/// </summary>
public sealed class ToolArgumentsTests
{
    [Theory]
    [InlineData("orphans", Scope.Orphans)]
    [InlineData("Caches", Scope.Caches)]
    [InlineData(" everything ", Scope.Everything)]
    public void Word_TheDescriptionOffers_IsRead(string word, Scope expected)
    {
        Assert.True(ToolArguments.TryRead<Scope>(word, out var scope));
        Assert.Equal(expected, scope);
    }

    [Theory]
    [InlineData("orphans,caches")]
    [InlineData("orphans, caches")]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(null)]
    public void Word_TheDescriptionDoesNotOffer_IsRefused(string? word) =>
        Assert.False(ToolArguments.TryRead<Scope>(word, out _));

    [Fact]
    public void Actions_JoinedByAComma_AreRefusedRatherThanReadAsTheirSum() =>
        Assert.False(ToolArguments.TryRead<RuleAction>("ignore,report", out _));
}
