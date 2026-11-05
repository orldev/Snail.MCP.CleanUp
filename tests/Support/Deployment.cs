using Microsoft.Extensions.Configuration;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>The environment a test starts the server in, written the way a client writes it.</summary>
/// <remarks>
/// A server under test is a real server on the real machine: one that lives past <see cref="Snail.MCP.CleanUp.Application.Watch.Warmup"/> takes
/// the watch of its own ledger and, with its sweep due at once, clears on its own what it may — Docker's build cache, the
/// remains of ended processes and the scratchpads of ended sessions under the scratch root. So the sweep is off
/// (<c>SWEEP_MINUTES=0</c>) and the scratch root is an empty folder of the test's own: nothing a test starts removes
/// anything of the developer's.
/// </remarks>
internal static class Deployment
{
    /// <summary>The folder a server under test takes for Claude Code's scratch root, inside the test's own.</summary>
    public static string ScratchRoot(Scratch scratch) => scratch.File("ScratchRoot");

    public static Dictionary<string, string> Variables(Scratch scratch, params (string Name, string Value)[] own)
    {
        Directory.CreateDirectory(ScratchRoot(scratch));

        var variables = new Dictionary<string, string>
        {
            [$"{EnvironmentSettings.Prefix}DATA_DIRECTORY"] = scratch.Path,
            [$"{EnvironmentSettings.Prefix}TRASH_DIRECTORY"] = scratch.File("Trash"),
            [$"{EnvironmentSettings.Prefix}SLEEP_OF_DIRECTORY"] = scratch.File("SleepOf"),
            [$"{EnvironmentSettings.Prefix}SWEEP_MINUTES"] = "0",
            [$"{EnvironmentSettings.Prefix}SCRATCH_ROOT"] = ScratchRoot(scratch)
        };

        foreach (var (name, value) in own)
            variables[$"{EnvironmentSettings.Prefix}{name}"] = value;

        return variables;
    }

    public static IConfiguration Settings(Scratch scratch, params (string Name, string Value)[] own) =>
        ServerConfigExtensions.Settings(Variables(scratch, own));
}
