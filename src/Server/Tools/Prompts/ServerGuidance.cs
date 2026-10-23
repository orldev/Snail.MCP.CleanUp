namespace Snail.MCP.CleanUp.Tools.Prompts;

/// <summary>What the client is told about this server when it connects.</summary>
/// <remarks>
/// The instructions are read once, before any tool is called, so they carry what a model cannot discover
/// by trying: which question each tool answers, and what each one costs.
/// </remarks>
public static class ServerGuidance
{
    /// <summary>The text that rides along with initialize.</summary>
    public const string Instructions =
        """
        This server keeps account of what agents leave behind on this machine — processes, ports, temporary
        files, Docker objects, iOS simulators, Android emulators, editor caches — and removes only what is
        proven to be an agent's. Its own tools are named cleanup_*.

        Look, decide, then act. cleanup_inventory says what is there and whose. cleanup_plan decides a cleanup
        and shows it; cleanup_apply carries out exactly that plan. Something merely seen appearing during a
        session is reported, never removed: proof is a name or label carrying the session, a program's
        environment naming it (CLAUDE_CODE_SESSION_ID), this server having started it, or having seen it run
        under the agent's own shells. On its own the server clears what nobody needs to see first: what
        sessions over for an hour left, the remains of ended processes, Docker's build cache once it is large.

        Make things on the record so that the proof exists. Start a long-running program with cleanup_run
        rather than in the background from a shell. Take a simulator with cleanup_simulator_acquire and an
        emulator with cleanup_emulator_acquire instead of using one somebody else may be driving, and let it go
        with cleanup_release. Label Docker objects with --label snail.owner=<session>; cleanup_diagnose says
        the value.

        The account also notices what nobody wrote a kind for: discoveries. When one keeps showing up, teach
        it with cleanup_learn. cleanup_keep spares something on purpose. find_tool finds a tool by a task in
        your own words.

        Every answer is JSON with an ok flag. When ok is false, read error and hint and change the call
        instead of repeating it.
        """;
}
