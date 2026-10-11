namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A benchmark of the real server on the real machine: minutes long, so run only when asked for.</summary>
/// <remarks>
/// Run with <c>SNAIL_CLEANUP_BENCH=1 dotnet test -c Release --filter Bench</c>. <c>SNAIL_CLEANUP_BENCH_LABEL</c> names
/// the run, <c>SNAIL_CLEANUP_BENCH_ENV</c> adds variables to the server as <c>NAME=value;NAME=value</c>, and
/// <c>SNAIL_CLEANUP_BENCH_SERVER</c> points at another build of it, such as a native one.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class BenchFactAttribute : FactAttribute
{
    public const string Variable = "SNAIL_CLEANUP_BENCH";

    public BenchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"a benchmark of minutes: set {Variable}=1 to run it";
    }
}
