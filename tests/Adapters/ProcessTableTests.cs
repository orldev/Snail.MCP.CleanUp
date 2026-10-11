using Snail.MCP.CleanUp.Adapters;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class ProcessTableTests
{
    /// <summary>Lines as ps printed them on 2026-10-11 under LC_ALL=C, a day of one digit padded with a second space.</summary>
    private const string Printed =
        """
            1     0 Sun Oct 11 14:04:06 2026     /sbin/launchd
        17181 17119 Sun Oct  4 19:56:24 2026     /Users/orldev/.vscode/extensions/anthropic.claude-code/native-binary/claude --output-format stream-json
        17203 17181 Sun Oct 11 19:56:25 2026     dotnet /mcp/Snail.MCP.Plans.dll  --twice  spaced
        not a process line
        """;

    [Fact]
    public void Lines_OfPs_AreReadWithTheirParentsStartsAndCommands()
    {
        var processes = ProcessTable.Parse(Printed);

        Assert.Equal([1, 17181, 17203], processes.Select(process => process.Pid));
        Assert.Equal(17181, processes[2].ParentPid);
        Assert.Equal(new DateTime(2026, 10, 4, 19, 56, 24), processes[1].StartedAt.DateTime);
        Assert.Equal("claude", processes[1].Program);
        Assert.Equal("dotnet /mcp/Snail.MCP.Plans.dll  --twice  spaced", processes[2].Command);
    }

    [Fact]
    public void Key_OfAProcess_CarriesItsStart_SoAReusedIdIsAnotherKey()
    {
        var processes = ProcessTable.Parse(Printed);

        Assert.Equal($"17181@{processes[1].StartedAt.ToUnixTimeSeconds()}", processes[1].Key);
    }
}
