using ModelContextProtocol.Client;
using Snail.MCP.CleanUp.Configuration;
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

    private static string Server => Path.Combine(AppContext.BaseDirectory, "Snail.MCP.CleanUp.dll");

    [Fact]
    public async Task Server_AsAProgram_AnnouncesItselfAndListsItsTools()
    {
        await using var client = await StartedAsync();

        var listed = await client.ListToolsAsync();

        Assert.Equal(ThisAssembly.Name, client.ServerInfo.Name);
        Assert.Equal(ServerGuidance.Instructions, client.ServerInstructions);
        Assert.Equal(Catalog.Names, [.. listed.Select(tool => tool.Name).Order(StringComparer.Ordinal)]);
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

    public void Dispose() => _scratch.Dispose();

    private Task<McpClient> StartedAsync() =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [Server],
            EnvironmentVariables = Deployment.Variables(_scratch).ToDictionary(variable => variable.Key, variable => (string?)variable.Value)
        }));
}
