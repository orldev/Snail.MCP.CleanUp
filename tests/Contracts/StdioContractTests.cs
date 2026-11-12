using ModelContextProtocol.Client;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.MCP.CleanUp.Tests.Contracts;

/// <summary>The server as a program: the real process, started the way a client starts it.</summary>
/// <remarks>
/// Only a process shows the one thing stdio can get wrong and nothing in-process can — a log line on
/// stdout, which is a broken JSON-RPC stream to the client and an unexplained hang to the person behind it.
/// </remarks>
public sealed class StdioContractTests : IDisposable
{
    private readonly Scratch _scratch = new();

    [Fact]
    public async Task Server_AsAProgram_AnnouncesItselfAndListsItsTools()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListToolsAsync();

        Assert.NotNull(client.ServerInfo);
        Assert.Equal("s3l-mcp-cleanup", client.ServerInfo.Name);
        Assert.Equal(ServerGuidance.Instructions, client.ServerInstructions);
        Assert.Equal(Catalog.Listed, [.. listed.Select(tool => tool.Name).Order(StringComparer.Ordinal)]);
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

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
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

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("stdio", answer.Data()!["server"]!["transport"]!.GetValue<string>());
        Assert.Equal(_scratch.Path, answer.Data()!["dataDirectory"]!.GetValue<string>());
    }

    /// <summary>A removal works as a job, and a job that ended in a refusal answers the refusal with its type, not a job done.</summary>
    [Fact]
    public async Task Apply_OfAPlanNeverMade_AnswersTheJobsRefusalWithItsType()
    {
        await using var client = await StartedAsync();

        var answer = await client.CallToolAsync("cleanup_apply", new Dictionary<string, object?> { ["planId"] = "nosuchplan", ["wait"] = 30 });

        Assert.True(answer.IsError);
        Assert.Contains("there is no plan nosuchplan", answer.Error(), StringComparison.Ordinal);
        Assert.Equal(Failures.NoSuchPlan, answer.Type());
    }

    /// <summary>A wait below zero is refused, never read as no wait at all.</summary>
    [Fact]
    public async Task Release_WithAWaitBelowZero_IsRefusedAsArguments()
    {
        await using var client = await StartedAsync();

        var answer = await client.CallToolAsync("cleanup_release", new Dictionary<string, object?> { ["id"] = "process:0@0", ["wait"] = -1 });

        Assert.True(answer.IsError);
        Assert.Equal(ToolFailures.Arguments, answer.Type());
    }

    /// <summary>Two scopes joined by a comma are no scope the description names: refused, never widened to everything.</summary>
    [Fact]
    public async Task Plan_ScopeOfTwoNamesJoinedByAComma_IsRefused()
    {
        await using var client = await StartedAsync();

        var answer = await client.CallToolAsync("cleanup_plan", new Dictionary<string, object?> { ["scope"] = "orphans,caches" });

        Assert.True(answer.IsError);
        Assert.False(answer.IsOk());
        Assert.Contains("there is no scope orphans,caches", answer.Error(), StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Arguments, answer.Type());
    }

    /// <summary>The server's own refusals carry their type through the program as far as a client reads it.</summary>
    [Fact]
    public async Task OwnRefusals_AsAProgram_CarryTheirType()
    {
        await using var client = await StartedAsync();

        var learned = await client.CallToolAsync("cleanup_learn", new Dictionary<string, object?> { ["pattern"] = "/nowhere/*", ["action"] = "forget" });
        var run = await client.CallToolAsync("cleanup_run", new Dictionary<string, object?> { ["command"] = "true", ["directory"] = _scratch.File("nowhere") });
        var applied = await client.CallToolAsync("cleanup_apply", new Dictionary<string, object?> { ["planId"] = "nosuchplan" });

        Assert.Equal(ToolFailures.Arguments, learned.Type());
        Assert.Equal(Reached.Absent, run.Type());
        Assert.Equal("noSuchPlan", applied.Type());
    }

    /// <summary>
    /// Every tool takes its arguments and answers in the envelope. A native build binds arguments with code generated ahead of
    /// time, and a type it was not given fails at the call, not at the build — so each tool is called once, with
    /// arguments that change nothing outside the test's own directory.
    /// </summary>
    [MachineFact]
    public async Task EveryTool_AsAProgram_TakesItsArgumentsAndAnswersInJson()
    {
        await using var client = await StartedAsync();

        var plan = await client.CallToolAsync("cleanup_plan", new Dictionary<string, object?> { ["scope"] = "caches" });
        var planId = plan.Data()?["planId"]?.GetValue<string>() ?? "none";

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
            Assert.Equal(answer.IsError == true, !answer.IsOk());
        }
    }

    /// <summary>A limit the server cannot run on stops it in one line naming the variable, with exit code 1 and no trace.</summary>
    [Fact]
    public async Task Server_WithAHistoryOfNoDays_RefusesItsStartInOneLine()
    {
        var environment = Deployment.Variables(_scratch, ("HISTORY_DAYS", "0")).ToDictionary(variable => variable.Key, variable => (string?)variable.Value);

        var exited = await Assert.ThrowsAsync<ServerExitedException>(() => ServerProgram.StartAsync("Snail.MCP.CleanUp", "SNAIL_CLEANUP_SERVER", environment));

        Assert.Equal(1, exited.ExitCode);
        Assert.Equal(
            "SNAIL_MCP_CLEANUP_HISTORY_DAYS (or HistoryDays in the settings file) is '0'; it is a number of days above zero.",
            exited.Stderr.Trim());
    }

    /// <summary>
    /// A setting that is not even of its type stops the start in one line naming the variable, exit code 1, no trace: the
    /// settings are bound before there is a host, and that binding refuses instead of crashing with 134.
    /// </summary>
    [Fact]
    public async Task Server_WithAHistoryThatIsNoNumber_RefusesItsStartInOneLine()
    {
        var environment = Deployment.Variables(_scratch, ("HISTORY_DAYS", "abc")).ToDictionary(variable => variable.Key, variable => (string?)variable.Value);

        var exited = await Assert.ThrowsAsync<ServerExitedException>(() => ServerProgram.StartAsync("Snail.MCP.CleanUp", "SNAIL_CLEANUP_SERVER", environment));
        var stderr = exited.Stderr.Trim();

        Assert.Equal(1, exited.ExitCode);
        Assert.Contains("SNAIL_MCP_CLEANUP_HISTORY_DAYS", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', stderr);
        Assert.DoesNotContain("   at ", stderr, StringComparison.Ordinal);
    }

    public void Dispose() => _scratch.Dispose();

    /// <summary>The server as built here, or the program <c>SNAIL_CLEANUP_SERVER</c> names, such as a native build of it, with its account in the test's folder.</summary>
    private Task<McpClient> StartedAsync() =>
        ServerProgram.StartAsync(
            "Snail.MCP.CleanUp",
            "SNAIL_CLEANUP_SERVER",
            Deployment.Variables(_scratch).ToDictionary(variable => variable.Key, variable => (string?)variable.Value));
}
