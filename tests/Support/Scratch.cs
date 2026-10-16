using System.Security.Cryptography;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A directory of the test's own, removed when the test is done with it.</summary>
/// <remarks>
/// Every test that starts the server or opens a ledger gets one. The default data directory is the one the
/// developer's real sessions keep their account in, and a suite that wrote there would leave rows in the
/// very ledger it is meant to keep clean — and could release what a real session owns.
/// <para>
/// It is named by the convention of the standard's tests, <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c> directly in the
/// temporary folder: a run that is killed before it removes its folders leaves ones this very server proves are leftovers,
/// where folders named at random under <c>snail-cleanup-tests</c> were discoveries nobody could prove ended (2026-10-16).
/// <c>TestFolder.Create()</c> of Snail.Sdk.Mcp.Testing names them so too, from the release after 0.1.0-local.33, which
/// this repository builds on and which does not carry it yet.
/// </para>
/// </remarks>
internal sealed class Scratch : IDisposable
{
    private static readonly string Prefix = Named();

    public Scratch() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Prefix}-{RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 8)}");

    public static string NewFolder() => new Scratch().Path;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }

    /// <summary>The test process and the second it started, in Unix seconds, as the system reports it.</summary>
    private static string Named()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();

        return $"s3l-test-{process.Id}-{new DateTimeOffset(process.StartTime).ToUnixTimeSeconds()}";
    }
}
