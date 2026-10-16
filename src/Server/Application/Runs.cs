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
/// <para>
/// A server that cannot tell its session still starts the program, on the record as nobody's: what nothing ties to an
/// owner is only ever reported, never removed, and the answer says so, as a lease refuses outright.
/// </para>
/// </remarks>
public sealed class Runs(ServerConfig config, IHolders sessions, ILedger ledger, IPrograms programs, IProcessTable table, TimeProvider clock)
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

        var process = programs.StartLogged(["/bin/zsh", "-c", command], workingDirectory, log);

        if (process.StartedAt is not { } started)
            return Answer.Ok(Ended(process.Pid, owner, log, workingDirectory));

        var key = $"{process.Pid}@{started.ToUnixTimeSeconds()}";
        var title = command.Length <= 120 ? command : $"{command[..120]}…";

        await ledger.RecordAsync(new Holding($"{Kinds.Process}:{key}", owner, Evidence.Ledger, clock.GetUtcNow(), title), cancellationToken);
        table.Refresh();

        return Answer.Ok(new JsonObject
        {
            ["pid"] = process.Pid,
            ["id"] = $"{Kinds.Process}:{key}",
            ["owner"] = owner.ToString(),
            ["log"] = log,
            ["directory"] = workingDirectory,
            ["note"] = owner.IsKnown ? null : Messages.RunOfNobody
        });
    }

    /// <summary>The answer for a program that ended before its start was read: nothing of it is left to write down, its log says how it went.</summary>
    private static JsonObject Ended(int pid, Owner owner, string log, string directory) => new()
    {
        ["pid"] = pid,
        ["id"] = null,
        ["owner"] = owner.ToString(),
        ["log"] = log,
        ["directory"] = directory,
        ["note"] = Messages.RunEndedAtOnce
    };

    private string LogFor(DateTimeOffset now)
    {
        var directory = Path.Combine(config.DataDirectory, "runs");

        Directory.CreateDirectory(directory);

        return Path.Combine(directory, $"{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("n")[..6]}.log");
    }
}
