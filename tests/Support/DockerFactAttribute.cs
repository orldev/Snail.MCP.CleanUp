using System.Diagnostics;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A machine test that needs Docker's daemon already running; skipped, never started, when it is not.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(MachineFactAttribute.Variable) != "1")
            Skip = $"reads the real machine: set {MachineFactAttribute.Variable}=1 to run it";
        else if (!IsDaemonUp())
            Skip = "Docker's daemon is not running, and a test does not start one";
    }

    private static bool IsDaemonUp()
    {
        try
        {
            using var info = Process.Start(new ProcessStartInfo("docker", ["info", "--format", "{{.ServerVersion}}"]) { RedirectStandardOutput = true, RedirectStandardError = true })!;

            return info.WaitForExit(10_000) && info.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
