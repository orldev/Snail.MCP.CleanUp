using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit.Abstractions;

namespace Snail.MCP.CleanUp.Tests.Machine;

/// <summary>The real server, started as a client starts it, answering about the real machine.</summary>
[Collection(OnThisMac.Name)]
public sealed class ServerOnThisMacTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>A model reads the whole answer; past this it crowds out the work it was asked for.</summary>
    private const int MostCharacters = 16_000;

    private readonly Scratch _scratch = new();

    private static string Server => Path.Combine(AppContext.BaseDirectory, "Snail.MCP.CleanUp.dll");

    [MachineFact]
    public async Task InventoryAndPlan_OfThisMac_AnswerInSecondsAndInAFewThousandCharacters()
    {
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Server],
            EnvironmentVariables = new Dictionary<string, string?> { ["SNAIL_MCP_CLEANUP_DATA_DIRECTORY"] = _scratch.Path }
        }));

        foreach (var (tool, arguments) in new (string, Dictionary<string, object?>)[]
                 {
                     ("cleanup_diagnose", []),
                     ("cleanup_inventory", []),
                     ("cleanup_plan", new() { ["scope"] = "everything" })
                 })
        {
            var timer = Stopwatch.StartNew();
            var answer = await client.CallToolAsync(tool, arguments);
            var text = answer.Text();

            output.WriteLine($"{tool}: {timer.Elapsed.TotalSeconds:0.0} s, {text.Length} characters");
            output.WriteLine(text.Length > 6000 ? $"{text[..6000]}…" : text);

            Assert.NotEqual(true, answer.IsError);
            Assert.InRange(text.Length, 1, MostCharacters);
            Assert.InRange(timer.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
