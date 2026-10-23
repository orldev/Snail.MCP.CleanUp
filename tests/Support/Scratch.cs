namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A directory of the test's own, removed when the test is done with it.</summary>
/// <remarks>
/// Every test that starts the server or opens a ledger gets one. The default data directory is the one the
/// developer's real sessions keep their account in, and a suite that wrote there would leave rows in the
/// very ledger it is meant to keep clean — and could release what a real session owns.
/// <para>
/// It is a <see cref="Snail.Sdk.Mcp.Testing.TestFolder"/>, named <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>: a run
/// that is killed before it removes its folders leaves ones this very server proves are leftovers, where folders named at
/// random under <c>snail-cleanup-tests</c> were discoveries nobody could prove ended (2026-10-16).
/// </para>
/// </remarks>
internal sealed class Scratch : IDisposable
{
    public string Path { get; } = Snail.Sdk.Mcp.Testing.TestFolder.Create();

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
