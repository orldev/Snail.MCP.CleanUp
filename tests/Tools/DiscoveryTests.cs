using ModelContextProtocol.Client;

namespace Snail.MCP.CleanUp.Tests.Tools;

/// <summary>
/// What a model asks when it does not know the name of the thing it wants. <c>find_tool</c> is Snail.Sdk.Mcp's; what is
/// this server's are the words its tools are known by, and these tests hold that a task in a model's own words still finds
/// the tool that does it — asked of the server as a client asks it.
/// </summary>
public sealed class DiscoveryTests(DiscoveryTests.Server server) : IClassFixture<DiscoveryTests.Server>
{
    [Theory]
    [InlineData("start my dev server in the background", "cleanup_run")]
    [InlineData("I need an iPhone simulator for my tests", "cleanup_simulator_acquire")]
    [InlineData("give me an android emulator", "cleanup_emulator_acquire")]
    [InlineData("what garbage did agents leave on this machine", "cleanup_inventory")]
    [InlineData("leave my postgres alone", "cleanup_keep")]
    [InlineData("teach it a new kind of leftover it discovered", "cleanup_learn")]
    public async Task FindTool_ATaskInAModelsOwnWords_FindsTheToolThatDoesIt(string task, string expected)
    {
        var answer = await FindAsync(task);

        Assert.True(answer["isMatched"]!.GetValue<bool>());
        Assert.Equal(expected, Named(answer)[0]);
    }

    [Fact]
    public async Task FindTool_AQuestionInNoWordsOfThisServer_IsAnsweredWithEveryTool()
    {
        var answer = await FindAsync("xyzzy plugh");

        Assert.False(answer["isMatched"]!.GetValue<bool>());
        Assert.NotNull(answer["note"]);
        Assert.Equal(Catalog.Listed, Named(answer).Order(StringComparer.Ordinal));
    }

    /// <summary>Asked in no words of its own, the server names its own tools first and the fleet's find_tool after them.</summary>
    [Fact]
    public async Task FindTool_AQuestionInNoWordsOfThisServer_NamesTheToolsOfItsPrefixFirst()
    {
        var named = Named(await FindAsync("xyzzy plugh"));
        var own = named.TakeWhile(name => name.StartsWith("cleanup_", StringComparison.Ordinal)).Count();

        Assert.Equal(Catalog.Names.Count, own);
        Assert.Equal(Catalog.Shared, named.Skip(own));
    }

    [Fact]
    public async Task FindTool_Answer_CarriesEveryArgumentTheToolTakes()
    {
        var apply = (await FindAsync("carry out the plan"))["tools"]!.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "cleanup_apply")!;
        var parameters = apply["parameters"]!.AsArray();

        Assert.True(parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == "planId")!["isRequired"]!.GetValue<bool>());
        Assert.Equal("string[]", parameters.Single(parameter => parameter!["name"]!.GetValue<string>() == "only")!["type"]!.GetValue<string>());
    }

    private async Task<JsonNode> FindAsync(string task)
    {
        var answer = await server.Client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = task, ["limit"] = 3 });

        Assert.True(answer.IsOk(), answer.Text());

        return answer.Data()!;
    }

    private static IReadOnlyList<string> Named(JsonNode answer) =>
        [.. answer["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>())];

    /// <summary>One server process for every question: find_tool reads the tool list, which a call does not change.</summary>
    public sealed class Server : IAsyncLifetime
    {
        private readonly Scratch _scratch = new();
        private McpClient? _client;

        public McpClient Client => _client ?? throw new InvalidOperationException("the server has not started");

        public async Task InitializeAsync() =>
            _client = await ServerProgram.StartAsync(
                "Snail.MCP.CleanUp",
                "SNAIL_CLEANUP_SERVER",
                Deployment.Variables(_scratch).ToDictionary(variable => variable.Key, variable => (string?)variable.Value));

        public async Task DisposeAsync()
        {
            if (_client is not null)
                await _client.DisposeAsync();

            _scratch.Dispose();
        }
    }
}
