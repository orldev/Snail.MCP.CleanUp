using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tests.Tools;

/// <summary>
/// Guard tests over what the model reads: a hardcoded description drifts from the code it describes
/// unless something fails when it does.
/// </summary>
public sealed partial class ToolCatalogTests
{
    /// <summary>The names every server of the fleet spells alike, without a prefix of its own.</summary>
    private static readonly string[] Shared = ["find_tool"];

    [Fact]
    public void EveryTool_InTheAssembly_IsNamedAndDescribed()
    {
        Assert.NotEmpty(Catalog.Tools);
        Assert.All(Catalog.Tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<McpServerToolAttribute>()!.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description));
        });
    }

    /// <summary>A parameter the model cannot see the meaning of is one it fills in by guessing.</summary>
    [Fact]
    public void EveryParameter_TheModelFills_IsDescribed()
    {
        var parameters = Catalog.Tools
            .SelectMany(tool => tool.GetParameters())
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .Where(parameter => parameter.ParameterType != typeof(McpServer));

        Assert.All(parameters, parameter =>
            Assert.False(string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<DescriptionAttribute>()?.Description), parameter.Name));
    }

    /// <summary>A client shows the tools of many servers in one list, and «plan» there is a question without an answer.</summary>
    [Fact]
    public void EveryTool_OfThisServer_CarriesItsPrefix()
    {
        Assert.All(Catalog.Names.Except(Shared), name => Assert.StartsWith("cleanup_", name, StringComparison.Ordinal));
        Assert.Equal(Catalog.Names.Count, Catalog.Names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A tool a text names and the server does not have is a call the model will make and lose.</summary>
    [Theory]
    [InlineData(typeof(ServerGuidance))]
    [InlineData(typeof(ToolDescriptions))]
    [InlineData(typeof(Messages))]
    public void EveryTool_ATextNames_Exists(Type texts)
    {
        var named = texts
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .SelectMany(field => ToolName().Matches((string)field.GetRawConstantValue()!))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);

        Assert.All(named, name => Assert.Contains(name, Catalog.Names));
    }

    /// <summary>
    /// The tool list is read into the context of every session before anything is asked, so its size is
    /// spent whether or not a tool is used. The platform this fleet serves measured 65 tools at 12 800 tokens,
    /// 39% of a 32k window; this server stays well under a tenth of that.
    /// </summary>
    [Fact]
    public void TheToolList_ReadIntoEverySession_StaysWithinItsBudget()
    {
        int characters = Catalog.Tools.Sum(tool =>
            tool.GetCustomAttribute<McpServerToolAttribute>()!.Name!.Length
            + tool.GetCustomAttribute<DescriptionAttribute>()!.Description.Length
            + tool.GetParameters().Sum(parameter => parameter.Name!.Length + (parameter.GetCustomAttribute<DescriptionAttribute>()?.Description.Length ?? 0)));

        Assert.InRange(characters, 1, 7168);
    }

    [GeneratedRegex(@"cleanup_[a-z_]+", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();
}
