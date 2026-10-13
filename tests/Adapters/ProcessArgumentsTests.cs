using System.Buffers.Binary;
using System.Text;
using Snail.MCP.CleanUp.Adapters.Native;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>The buffer of <c>KERN_PROCARGS2</c> written by hand: count, path, padding, arguments, environment.</summary>
public sealed class ProcessArgumentsTests
{
    private const string Session = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private static byte[] Buffer(string path, string[] arguments, string[] environment, string trailer = "\0executable_path=/x\0")
    {
        var text = new StringBuilder();

        text.Append(path).Append("\0\0\0\0");

        foreach (var argument in arguments)
            text.Append(argument).Append('\0');

        foreach (var variable in environment)
            text.Append(variable).Append('\0');

        text.Append(trailer);

        var body = Encoding.UTF8.GetBytes(text.ToString());
        var buffer = new byte[sizeof(int) + body.Length];

        BinaryPrimitives.WriteInt32LittleEndian(buffer, arguments.Length);
        body.CopyTo(buffer, sizeof(int));

        return buffer;
    }

    /// <summary>The shape of the detached Xcode Python of 2026-10-13: a server of http.server, its session in its environment.</summary>
    [Fact]
    public void Environment_ThatNamesTheSession_GivesTheSessionAndTheAgent()
    {
        var buffer = Buffer(
            "/Applications/Xcode.app/Contents/Developer/usr/bin/python3",
            ["python3", "-m", "http.server", "8000"],
            ["PATH=/usr/bin", $"CLAUDE_CODE_SESSION_ID={Session}", "CLAUDE_PID=49777", "HOME=/Users/me"]);

        var parsed = ProcessArguments.Parse(buffer, withEnvironment: true);

        Assert.Equal(["python3", "-m", "http.server", "8000"], parsed.Words!);
        Assert.Equal(Session, parsed.Session);
        Assert.Equal(49777, parsed.Agent);
    }

    [Fact]
    public void Environment_WithoutTheVariables_GivesNoSession()
    {
        var parsed = ProcessArguments.Parse(Buffer("/bin/sleep", ["sleep", "60"], ["PATH=/usr/bin", "CLAUDE_CODE_SESSION_IDX=nope"]), withEnvironment: true);

        Assert.Equal(["sleep", "60"], parsed.Words!);
        Assert.Null(parsed.Session);
        Assert.Null(parsed.Agent);
    }

    /// <summary>The variables of the apple strings after the empty one are not the environment.</summary>
    [Fact]
    public void Strings_AfterTheEndOfTheEnvironment_AreNotRead()
    {
        var buffer = Buffer("/bin/sleep", ["sleep"], ["PATH=/usr/bin"], $"\0CLAUDE_CODE_SESSION_ID={Session}\0");

        Assert.Null(ProcessArguments.Parse(buffer, withEnvironment: true).Session);
    }

    /// <summary>Another user's process: its environment is not this server's to act on, even when the kernel shows it.</summary>
    [Fact]
    public void Environment_NotToBeRead_IsNotRead()
    {
        var buffer = Buffer("/usr/bin/python3", ["python3"], [$"CLAUDE_CODE_SESSION_ID={Session}"]);

        Assert.Null(ProcessArguments.Parse(buffer, withEnvironment: false).Session);
    }

    /// <summary>A buffer cut in the middle of the value: what is left of the id may be the start of another one.</summary>
    [Fact]
    public void Buffer_CutInTheMiddleOfTheVariable_ProvesNothing()
    {
        var whole = Buffer("/usr/bin/python3", ["python3"], [$"CLAUDE_CODE_SESSION_ID={Session}", "CLAUDE_PID=4"]);
        var cut = whole.AsSpan(0, Array.IndexOf(whole, (byte)'-') + 3);

        var parsed = ProcessArguments.Parse(cut, withEnvironment: true);

        Assert.Equal(["python3"], parsed.Words!);
        Assert.Null(parsed.Session);
    }

    [Fact]
    public void Buffer_CutInTheArguments_ReadsNoEnvironment()
    {
        var whole = Buffer("/usr/bin/python3", ["python3", "-m", "http.server"], [$"CLAUDE_CODE_SESSION_ID={Session}"]);
        var cut = whole.AsSpan(0, Encoding.UTF8.GetByteCount("/usr/bin/python3\0\0\0\0python3\0-m\0htt") + sizeof(int));

        Assert.Null(ProcessArguments.Parse(cut, withEnvironment: true).Session);
    }

    [Theory]
    [InlineData("CLAUDE_CODE_SESSION_ID=")]
    [InlineData("CLAUDE_CODE_SESSION_ID=a b")]
    [InlineData("CLAUDE_CODE_SESSION_ID=../../etc")]
    public void Value_ThatIsNoSessionId_IsNotBelieved(string variable) =>
        Assert.Null(ProcessArguments.Parse(Buffer("/bin/x", ["x"], [variable]), withEnvironment: true).Session);

    [Fact]
    public void Value_LongerThanAnyId_IsNotBelieved() =>
        Assert.Null(ProcessArguments.Parse(Buffer("/bin/x", ["x"], [$"CLAUDE_CODE_SESSION_ID={new string('a', 200)}"]), withEnvironment: true).Session);

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 0 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 0x2F })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x2F, 0, 0x61 })]
    public void Buffer_ThatIsNoneOfThis_GivesNothingAndDoesNotThrow(byte[] buffer) =>
        Assert.Null(ProcessArguments.Parse(buffer, withEnvironment: true).Session);
}
