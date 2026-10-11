using System.Diagnostics;
using System.Globalization;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Tools.Prompts;

namespace Snail.MCP.CleanUp.Application;

/// <summary>Starts long-running programs on an agent's behalf and writes them down at birth.</summary>
/// <remarks>
/// A program started this way needs no inference about whose it is: the ledger says so from its first
/// second, by its id and the moment it started, which no later program will share. Its output goes to a
/// log of its own, never to this server's stdout — that is the JSON-RPC stream, and one stray line from a
/// dev server there breaks the session.
/// </remarks>
public sealed class Runs(ServerConfig config, ISessions sessions, ILedger ledger, IProcessTable table, TimeProvider clock)
{
    /// <summary>Starts the command in the directory and answers with its process and its log.</summary>
    public async Task<Answer> StartAsync(string command, string? directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
            return Answer.Failed(Messages.RunNeedsACommand, Messages.RunHint);

        var workingDirectory = string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory;

        if (!Directory.Exists(workingDirectory))
            return Answer.Failed($"there is no directory {workingDirectory}", Messages.RunHint);

        var owner = await sessions.CurrentAsync(cancellationToken);
        var log = LogFor(clock.GetLocalNow());

        using var process = Process.Start(Started(command, workingDirectory, log))!;

        var key = $"{process.Id}@{new DateTimeOffset(process.StartTime).ToUnixTimeSeconds()}";
        var title = command.Length <= 120 ? command : $"{command[..120]}…";

        await ledger.RecordAsync(new Holding($"{Kinds.Process}:{key}", owner, Evidence.Ledger, clock.GetUtcNow(), title), cancellationToken);
        table.Refresh();

        return Answer.Ok(new JsonObject
        {
            ["pid"] = process.Id,
            ["id"] = $"{Kinds.Process}:{key}",
            ["owner"] = owner.ToString(),
            ["log"] = log,
            ["directory"] = workingDirectory
        });
    }

    private string LogFor(DateTimeOffset now)
    {
        var directory = Path.Combine(config.DataDirectory, "runs");

        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("n")[..6]}.log");
    }

    /// <summary>
    /// The shell redirects its own output before it runs anything, then hands its process over to the command:
    /// the program keeps the id that was written down, and the log is the only place it can write to.
    /// </summary>
    private static ProcessStartInfo Started(string command, string directory, string log)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            WorkingDirectory = directory,
            UseShellExecute = false
        };

        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec >\"$0\" 2>&1 </dev/null; exec /bin/zsh -c \"$1\"");
        start.ArgumentList.Add(log);
        start.ArgumentList.Add(command);

        return start;
    }
}
