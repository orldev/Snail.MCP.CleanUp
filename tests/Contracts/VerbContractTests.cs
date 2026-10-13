using System.Diagnostics;

namespace Snail.MCP.CleanUp.Tests.Contracts;

/// <summary>The program as Claude Code's hooks and a person call it: a verb, an answer, an exit.</summary>
public sealed class VerbContractTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private static string Server => Path.Combine(AppContext.BaseDirectory, "Snail.MCP.CleanUp.dll");

    /// <summary>A hook's stdout is read by Claude Code; a hook that says nothing and exits 0 never gets in the agent's way.</summary>
    [Fact]
    public async Task Hook_ForAStartingSession_ExitsCleanlyAndSaysNothing()
    {
        var (code, output) = await RunAsync("""{"hook_event_name":"SessionStart","session_id":"contract-test"}""", "hook");

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, output.Trim());
    }

    [Fact]
    public async Task InstallHooks_WritesTheSettingsItWasPointedAt()
    {
        var (code, output) = await RunAsync(string.Empty, "install-hooks");

        Assert.Equal(0, code);
        Assert.Contains("installed", output, StringComparison.Ordinal);
        Assert.Contains("Snail.MCP.CleanUp.dll hook", await File.ReadAllTextAsync(_scratch.File("settings.json")), StringComparison.Ordinal);
    }

    private async Task<(int Code, string Output)> RunAsync(string input, string verb)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };

        start.ArgumentList.Add(Server);
        start.ArgumentList.Add(verb);

        foreach (var (name, value) in Deployment.Variables(_scratch, ("CLAUDE_SETTINGS", _scratch.File("settings.json")), ("CONFIG", Snail.Sdk.Mcp.Testing.ServerProgram.NoSettingsFile)))
            start.Environment[name] = value;

        using var process = Process.Start(start)!;

        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, output);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
