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
