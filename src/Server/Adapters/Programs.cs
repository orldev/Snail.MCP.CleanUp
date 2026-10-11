using System.ComponentModel;
using System.Diagnostics;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Runs outside programs with a deadline and the C locale.</summary>
/// <remarks>
/// The C locale is not a nicety: <c>ps</c> prints its start times in the language of the machine, and a
/// parser written against English dates reads nothing on a machine set to Russian. A program that is not
/// installed is an answer, not an exception, because a Mac without Android tools is an ordinary Mac.
/// </remarks>
public sealed class Programs : IPrograms
{
    /// <inheritdoc/>
    public async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = Started(program, arguments) };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return ProgramRun.Missing(program);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token);

            return new ProgramRun(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return new ProgramRun(-1, string.Empty, string.Empty, IsTimedOut: true);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static ProcessStartInfo Started(string program, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["LC_ALL"] = "C";
        start.Environment["LANG"] = "C";

        return start;
    }
}
