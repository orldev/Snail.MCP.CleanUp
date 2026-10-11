using Snail.MCP.CleanUp.Tools;

namespace Snail.MCP.CleanUp.Tests.Tools;

/// <summary>What a model asks when it does not know the name of the thing it wants.</summary>
public sealed class ToolIndexTests
{
    private readonly ToolIndex _index = new();

    [Theory]
    [InlineData("start my dev server in the background", "cleanup_run")]
    [InlineData("I need an iPhone simulator for my tests", "cleanup_simulator_acquire")]
    [InlineData("give me an android emulator", "cleanup_emulator_acquire")]
    [InlineData("what garbage did agents leave on this machine", "cleanup_inventory")]
    [InlineData("leave my postgres alone", "cleanup_keep")]
    [InlineData("teach it a new kind of leftover it discovered", "cleanup_learn")]
    public void TaskInAModelsOwnWords_FindsTheToolThatDoesIt(string task, string expected) =>
        Assert.Equal(expected, _index.Find(task, 3)[0].Name);

    [Fact]
    public void QuestionInNoWordsOfThisServer_IsAnsweredWithEveryTool()
    {
        var answer = _index.Answer("xyzzy plugh", 5);

        Assert.False(answer["isMatched"]!.GetValue<bool>());
        Assert.Equal(ToolIndex.All.Count, answer["tools"]!.AsArray().Count);
    }

    [Fact]
    public void Answer_CarriesEveryArgumentTheToolTakes()
    {
        var apply = _index.Answer("carry out the plan", 5)["tools"]!.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "cleanup_apply")!;
        var parameters = apply["parameters"]!.AsArray();

        Assert.True(parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == "planId")!["isRequired"]!.GetValue<bool>());
        Assert.Equal("string[]", parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == "only")!["type"]!.GetValue<string>());
    }
}
