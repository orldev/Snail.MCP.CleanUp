using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Application;

public sealed class HookInstallationTests : IDisposable
{
    private const string SleepOf = "\"/Users/dev/Library/Application Support/Snail.SleepOf/sleepof-hook\"";

    private const string Command = "dotnet /tools/Snail.MCP.CleanUp.dll hook";

    private readonly Scratch _scratch = new();

    private HookInstallation Installation => new(new ServerConfig { ClaudeSettings = _scratch.File("settings.json") });

    private JsonObject Settings => JsonNode.Parse(File.ReadAllText(_scratch.File("settings.json")))!.AsObject();

    public HookInstallationTests() =>
        File.WriteAllText(_scratch.File("settings.json"), $$"""
            {
              "model": "opus",
              "hooks": {
                "Stop": [ { "hooks": [ { "type": "command", "command": {{JsonValue.Create(SleepOf).ToJsonString()}}, "timeout": 3600 } ] } ],
                "SessionEnd": [ { "hooks": [ { "type": "command", "command": {{JsonValue.Create(SleepOf).ToJsonString()}}, "timeout": 5 } ] } ]
              }
            }
            """);

    private IReadOnlyList<string> Commands(string name) =>
        [.. Settings["hooks"]![name]!.AsArray().Select(entry => entry!["hooks"]![0]!["command"]!.GetValue<string>())];

    /// <summary>The file is shared with SleepOf, memory servers and the person; their entries stay exactly as they were.</summary>
    [Fact]
    public void Install_AddsItsOwnEntries_BesideEveryoneElses()
    {
        Installation.Install(Command);

        Assert.Equal([SleepOf, Command], Commands("Stop"));
        Assert.Equal([SleepOf, Command], Commands("SessionEnd"));
        Assert.Equal([Command], Commands("SessionStart"));
        Assert.Equal("opus", Settings["model"]!.GetValue<string>());
    }

    [Fact]
    public void Install_Twice_LeavesOneEntryPerEvent()
    {
        Installation.Install(Command);
        Installation.Install(Command);

        Assert.Equal([SleepOf, Command], Commands("Stop"));
    }

    [Fact]
    public void Uninstall_TakesOutOnlyItsOwn()
    {
        Installation.Install(Command);
        Installation.Uninstall();

        Assert.Equal([SleepOf], Commands("Stop"));
        Assert.Null(Settings["hooks"]!["SessionStart"]);
    }

    [Fact]
    public void Install_KeepsACopyOfTheFileAsItFoundIt()
    {
        var before = File.ReadAllText(_scratch.File("settings.json"));

        Installation.Install(Command);

        Assert.Equal(before, File.ReadAllText(_scratch.File("settings.json.cleanup-backup")));
    }

    public void Dispose() => _scratch.Dispose();
}
