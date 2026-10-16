using System.ComponentModel;
using System.Diagnostics;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Runs outside programs with a deadline and the C locale.</summary>
/// <remarks>
/// The C locale is not a nicety: <c>ps</c> prints its start times in the language of the machine, and a
/// parser written against English dates reads nothing on a machine set to Russian. A program that is not
/// installed is an answer, not an exception, because a Mac without Android tools is an ordinary Mac.
/// </remarks>
public sealed class Programs(Snail.MCP.CleanUp.Application.Performance performance, TimeProvider clock) : IPrograms
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset At, Task<ProgramRun> Run)> _readings = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<ProgramRun> ReadAsync(string program, IReadOnlyList<string> arguments, TimeSpan freshness, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var key = $"{program}\0{string.Join('\0', arguments)}";
        var now = clock.GetUtcNow();

        var reading = _readings.AddOrUpdate(
            key,
            _ => (now, RunAsync(program, arguments, timeout, CancellationToken.None)),
            (_, held) => now - held.At <= freshness ? held : (now, RunAsync(program, arguments, timeout, CancellationToken.None)));

        return reading.Run.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            return await RanAsync(program, arguments, timeout, cancellationToken);
        }
        finally
        {
            performance.Ran(program, arguments, Stopwatch.GetElapsedTime(started));
        }
    }

    private static async Task<ProgramRun> RanAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
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

    /// <inheritdoc/>
    /// <remarks>
    /// The shell sends its own output to the log before it runs anything, then hands its process over to the program: the
    /// program keeps the id that is answered, and the log is the only place it can write to — this server's stdout is the
    /// JSON-RPC stream, and one stray line from a dev server there breaks the session.
    /// </remarks>
    public StartedProgram StartLogged(IReadOnlyList<string> command, string? directory, string log)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };

        if (directory is not null)
            start.WorkingDirectory = directory;

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec >\"$0\" 2>&1 </dev/null; exec \"$@\"");
        start.ArgumentList.Add(log);

        foreach (var word in command)
            start.ArgumentList.Add(word);

        using var process = Process.Start(start)!;

        return new StartedProgram(process.Id, StartOf(process));
    }

    /// <inheritdoc/>
    /// <remarks>The shell puts the program in the background and ends at once, so the program is a child of nothing that waits for it.</remarks>
    public async Task<bool> TryDetachAsync(IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("\"$0\" \"$@\" >/dev/null 2>&1 </dev/null &");

        foreach (var word in command)
            start.ArgumentList.Add(word);

        try
        {
            using var started = Process.Start(start);
            using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            patience.CancelAfter(TimeSpan.FromSeconds(5));

            if (started is not null)
                await started.WaitForExitAsync(patience.Token);

            return started is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    /// <summary>When a program started; null when it ended before that could be read.</summary>
    /// <remarks>
    /// The start is read from the kernel after the program is started, and the kernel forgets a program that has ended and
    /// been reaped: half of twenty runs of <c>exit 3</c> threw instead of answering (2026-10-16).
    /// </remarks>
    private static DateTimeOffset? StartOf(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ended) when (ended is Win32Exception or InvalidOperationException)
        {
            return null;
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
