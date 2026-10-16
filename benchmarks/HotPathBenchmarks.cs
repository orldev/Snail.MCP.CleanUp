using BenchmarkDotNet.Attributes;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Trails;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Snail.MCP.CleanUp.Benchmarks;

/// <summary>The functions a census runs thousands of times: reading the process table, ps and lsof.</summary>
/// <remarks>Matching rules and deciding a plan are Snail.Toolkit.Cleanup's, and measured by its own benchmarks.</remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class HotPathBenchmarks
{
    private string _ps = string.Empty;

    private string _lsof = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _ps = Run("ps", "-axww", "-o", "pid=,ppid=,lstart=,command=");
        _lsof = Run("lsof", "-nP", "-iTCP", "-sTCP:LISTEN", "-Fpcn");
    }

    [Benchmark]
    public int ParsePs() => ProcessTable.Parse(_ps).Count;

    /// <summary>The whole process table read from the kernel, as a census reads it on macOS.</summary>
    [Benchmark]
    public int ReadProcessTable() => Adapters.Native.LibProc.List().Count;

    [Benchmark]
    public int ParseLsof() => PortTrail.Parse(_lsof).Count;

    private static string Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true };

        start.Environment["LC_ALL"] = "C";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return output;
    }
}
