using ModelContextProtocol.Client;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Tests.Contracts;

/// <summary>The server as a program: the real process, started the way a client starts it.</summary>
/// <remarks>
/// Only a process shows the one thing stdio can get wrong and nothing in-process can — a log line on
/// stdout, which is a broken JSON-RPC stream to the client and an unexplained hang to the person behind it.
/// </remarks>
public sealed class StdioContractTests : IDisposable
{
    private readonly Scratch _scratch = new();

    /// <summary>The server as built here, or the program <c>SNAIL_CLEANUP_SERVER</c> names, such as a native build of it.</summary>
    private static (string Command, string[] Arguments) Server => Snail.Sdk.Mcp.Testing.ServerProgram.Command("Snail.MCP.CleanUp", "SNAIL_CLEANUP_SERVER");

    [Fact]
    public async Task Server_AsAProgram_AnnouncesItselfAndListsItsTools()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListToolsAsync();

        Assert.Equal("s3l-mcp-cleanup", client.ServerInfo.Name);
        Assert.Equal(ServerGuidance.Instructions, client.ServerInstructions);
        Assert.Equal(Catalog.Names, [.. listed.Select(tool => tool.Name).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The guard rules of every Snail server, on the list a model receives: names in snake case and listed once, the tool
    /// and every parameter described, read-only or destructive stated, every allowed value named, English.
    /// </summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListToolsAsync();

        Assert.Empty(Snail.Sdk.Mcp.Testing.ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// <summary>The prompt a client offers as a slash command: listed with its argument described, and asking for agreement before any removal.</summary>
    [Fact]
    public async Task CleanupPrompt_AsAProgram_IsListedAndWaitsForAgreementBeforeApplying()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListPromptsAsync();
        var prompt = await client.GetPromptAsync("cleanup", new Dictionary<string, object?> { ["scope"] = "caches" });
        var cleanup = Assert.Single(listed).ProtocolPrompt;
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(prompt.Messages).Content).Text;

        Assert.Equal("cleanup", cleanup.Name);
        Assert.Equal(ToolDescriptions.CleanupPrompt, cleanup.Description);
        Assert.Equal(ToolDescriptions.CleanupPromptScope, Assert.Single(cleanup.Arguments!).Description);
        Assert.Contains("Call cleanup_plan with scope caches.", text, StringComparison.Ordinal);
        Assert.Contains("Do not call cleanup_apply before it.", text, StringComparison.Ordinal);
    }

    /// <summary>A scope the prompt does not name is refused, as cleanup_plan refuses it, never widened to everything.</summary>
    [Fact]
    public async Task CleanupPrompt_ScopeOfTwoNamesJoinedByAComma_IsRefused()
    {
        await using var client = await StartedAsync();

        var refusal = await Assert.ThrowsAnyAsync<ModelContextProtocol.McpException>(() =>
            client.GetPromptAsync("cleanup", new Dictionary<string, object?> { ["scope"] = "orphans,caches" }).AsTask());

        Assert.Contains("there is no scope orphans,caches", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnose_AsAProgram_SaysItSpeaksStdioAndKeepsItsAccountWhereItWasTold()
    {
        await using var client = await StartedAsync();

        var answer = await client.CallToolAsync("cleanup_diagnose", new Dictionary<string, object?>());

        Assert.NotEqual(true, answer.IsError);
        Assert.Equal("stdio", answer.Data()["server"]!["transport"]!.GetValue<string>());
        Assert.Equal(_scratch.Path, answer.Data()["dataDirectory"]!.GetValue<string>());
    }

    /// <summary>Two scopes joined by a comma are no scope the description names: refused, never widened to everything.</summary>
    [Fact]
    public async Task Plan_ScopeOfTwoNamesJoinedByAComma_IsRefused()
    {
        await using var client = await StartedAsync();

        var answer = await client.CallToolAsync("cleanup_plan", new Dictionary<string, object?> { ["scope"] = "orphans,caches" });

        Assert.True(answer.IsError);
        Assert.Contains("there is no scope orphans,caches", JsonNode.Parse(answer.Text())!["error"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every tool takes its arguments and answers in JSON. A native build binds arguments with code generated ahead of
    /// time, and a type it was not given fails at the call, not at the build — so each tool is called once, with
    /// arguments that change nothing outside the test's own directory.
    /// </summary>
    [MachineFact]
    public async Task EveryTool_AsAProgram_TakesItsArgumentsAndAnswersInJson()
    {
        await using var client = await StartedAsync();

        var plan = await client.CallToolAsync("cleanup_plan", new Dictionary<string, object?> { ["scope"] = "caches" });
        var planId = plan.Data()["planId"]?.GetValue<string>() ?? "none";

        Dictionary<string, Dictionary<string, object?>> calls = new()
        {
            ["cleanup_inventory"] = [],
            ["cleanup_apply"] = new() { ["planId"] = planId, ["only"] = new[] { "nothing:here" } },
            ["cleanup_release"] = new() { ["id"] = "process:0@0" },
            ["cleanup_keep"] = new() { ["id"] = "file:/nowhere", ["reason"] = "a test", ["withdraw"] = false },
            ["cleanup_learn"] = new() { ["trail"] = "file", ["pattern"] = "/nowhere/*", ["action"] = "ignore", ["note"] = "a test" },
            ["cleanup_run"] = new() { ["command"] = "true", ["directory"] = _scratch.Path },
            ["cleanup_simulator_acquire"] = new() { ["from"] = "no such device of any kind", ["boot"] = false },
            ["cleanup_emulator_acquire"] = new() { ["image"] = "no-such-system-image", ["window"] = false },
            ["cleanup_diagnose"] = [],
            ["find_tool"] = new() { ["task"] = "remove what docker left", ["limit"] = 3 }
        };

        foreach (var (tool, arguments) in calls)
        {
            var answer = await client.CallToolAsync(tool, arguments);
            var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(answer.Content)).Text;

            Assert.True(JsonNode.Parse(text)?["ok"] is not null, $"{tool} answered {text}");
        }
    }

    public void Dispose() => _scratch.Dispose();

    private Task<McpClient> StartedAsync() =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = Server.Command,
            Arguments = Server.Arguments,
            EnvironmentVariables = Deployment.Variables(_scratch, ("CONFIG", Snail.Sdk.Mcp.Testing.ServerProgram.NoSettingsFile)).ToDictionary(variable => variable.Key, variable => (string?)variable.Value)
        }));
}
