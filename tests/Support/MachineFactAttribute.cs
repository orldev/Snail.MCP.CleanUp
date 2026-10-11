namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A test that reads or changes the real machine it runs on, skipped — not passed — unless asked for.</summary>
/// <remarks>
/// Run with <c>SNAIL_CLEANUP_MACHINE=1 dotnet test</c>. These tests create only what they remove themselves
/// and name it so that nothing of the developer's own can be mistaken for it, but they still start real
/// programs, so a plain test run never does.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class MachineFactAttribute : FactAttribute
{
    public const string Variable = "SNAIL_CLEANUP_MACHINE";

    public MachineFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"reads the real machine: set {Variable}=1 to run it";
    }
}

/// <summary>The tests that use the real machine run one class at a time.</summary>
/// <remarks>
/// A simulator booting for the first time keeps CoreSimulator busy for most of a minute, and a census run
/// beside it waited past its deadline for <c>simctl list</c> — measured on 2026-10-11, when the suite failed
/// in parallel and passed alone.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OnThisMac
{
    public const string Name = "on this Mac";
}
