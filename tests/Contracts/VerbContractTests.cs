using System.Diagnostics;

namespace Snail.MCP.CleanUp.Tests.Contracts;

/// <summary>The program as Claude Code's hooks and a person call it: a verb, an answer, an exit.</summary>
public sealed class VerbContractTests : IDisposable
{
    private readonly Scratch _scratch = new();

    /// <summary>The server as built here, or the program <c>SNAIL_CLEANUP_SERVER</c> names, such as a native build of it.</summary>
    private static (string Program, string[] Arguments) Server => ServerProgram.Command("Snail.MCP.CleanUp", "SNAIL_CLEANUP_SERVER");

    /// <summary>The file a hook line of this server names: the dll run by dotnet, or the native program itself.</summary>
    private static string Started => Path.GetFileName(Server.Arguments.LastOrDefault() ?? Server.Program);

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
        Assert.Contains($"{Started} hook", await File.ReadAllTextAsync(_scratch.File("settings.json")), StringComparison.Ordinal);
    }

    private async Task<(int Code, string Output)> RunAsync(string input, string verb)
    {
        var (program, arguments) = Server;
        var start = new ProcessStartInfo(program) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.ArgumentList.Add(verb);
        start.Environment["HOME"] = ServerProgram.Home;

        foreach (var (name, value) in Deployment.Variables(_scratch, ("CLAUDE_SETTINGS", _scratch.File("settings.json")), ("CONFIG", ServerProgram.NoSettingsFile)))
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
        _scratch.Dispose();
    }
}
