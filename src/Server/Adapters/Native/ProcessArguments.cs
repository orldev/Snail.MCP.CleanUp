using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Snail.MCP.CleanUp.Adapters.Native;

/// <summary>The buffer <c>KERN_PROCARGS2</c> fills: the arguments of a process, and after them its environment.</summary>
/// <remarks>
/// The buffer is a count, the path the program was started from, padding, the arguments one after another, then the
/// environment the same way, each string ended by a zero, and an empty string after the last variable. Of the
/// environment only two variables are read, and nothing else of it is made into a string: Claude Code gives every
/// program it starts <c>CLAUDE_CODE_SESSION_ID</c> and <c>CLAUDE_PID</c>, and a program keeps them when it detaches —
/// a <c>python -m http.server</c> started with <c>( … &amp; )</c> lived seven hours as a child of launchd on
/// 2026-10-13, and only its environment still named the session.
/// <para>
/// A buffer the kernel cut short proves nothing past the cut: a variable without its ending zero may be cut in the
/// middle of its value, so it is not believed.
/// </para>
/// </remarks>
internal static class ProcessArguments
{
    /// <summary>The variable Claude Code names the session with.</summary>
    public const string SessionVariable = "CLAUDE_CODE_SESSION_ID";

    /// <summary>The variable Claude Code names its own process with.</summary>
    public const string AgentVariable = "CLAUDE_PID";

    /// <summary>The longest session id believed: a session id is a GUID, and a longer value is not one Claude Code wrote.</summary>
    public const int LongestSession = 128;

    private static ReadOnlySpan<byte> SessionPrefix => "CLAUDE_CODE_SESSION_ID="u8;

    private static ReadOnlySpan<byte> AgentPrefix => "CLAUDE_PID="u8;

    /// <summary>The arguments of a process, and its session and agent when <paramref name="withEnvironment"/> lets its environment be read.</summary>
    public static Parsed Parse(ReadOnlySpan<byte> bytes, bool withEnvironment)
    {
        if (bytes.Length < sizeof(int))
            return default;

        var count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(bytes), 0, bytes.Length);
        var at = sizeof(int);

        at += Math.Max(bytes[at..].IndexOf((byte)0), 0);

        while (at < bytes.Length && bytes[at] == 0)
            at++;

        var words = new List<string>(count);

        while (words.Count < count && at < bytes.Length)
        {
            var end = bytes[at..].IndexOf((byte)0);
            var length = end < 0 ? bytes.Length - at : end;

            words.Add(Encoding.UTF8.GetString(bytes.Slice(at, length)));
            at += length + 1;
        }

        var parsed = new Parsed(words.Count > 0 ? words : null, null, null);

        return withEnvironment && words.Count == count ? Environment(bytes, at, parsed) : parsed;
    }

    /// <summary>The two variables read from the environment that begins at <paramref name="at"/>; the first of each counts, as for <c>getenv</c>.</summary>
    private static Parsed Environment(ReadOnlySpan<byte> bytes, int at, Parsed parsed)
    {
        while (at < bytes.Length && bytes[at] != 0)
        {
            var end = bytes[at..].IndexOf((byte)0);

            if (end < 0)
                return parsed;

            var variable = bytes.Slice(at, end);

            if (parsed.Session is null && variable.StartsWith(SessionPrefix))
                parsed = parsed with { Session = Session(variable[SessionPrefix.Length..]) };
            else if (parsed.Agent is null && variable.StartsWith(AgentPrefix))
                parsed = parsed with { Agent = Agent(variable[AgentPrefix.Length..]) };

            at += end + 1;
        }

        return parsed;
    }

    /// <summary>A session id as Claude Code writes one: letters, digits, dashes and underscores, not too long.</summary>
    private static string? Session(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty || value.Length > LongestSession)
            return null;

        foreach (var letter in value)
        {
            if (!char.IsAsciiLetterOrDigit((char)letter) && letter is not (byte)'-' and not (byte)'_')
                return null;
        }

        return Encoding.ASCII.GetString(value);
    }

    private static int? Agent(ReadOnlySpan<byte> value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 1 ? pid : null;

    /// <summary>What the buffer said.</summary>
    /// <param name="Words">The arguments, the program's name first; null when there are none.</param>
    /// <param name="Session">The session its environment names; null when it names none or was not read.</param>
    /// <param name="Agent">The agent's process its environment names; null when it names none or was not read.</param>
    internal readonly record struct Parsed(List<string>? Words, string? Session, int? Agent);
}
