namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A directory of the test's own, removed when the test is done with it.</summary>
/// <remarks>
/// Every test that starts the server or opens a ledger gets one. The default data directory is the one the
/// developer's real sessions keep their account in, and a suite that wrote there would leave rows in the
/// very ledger it is meant to keep clean — and could release what a real session owns.
/// </remarks>
internal sealed class Scratch : IDisposable
{
    public Scratch() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snail-cleanup-tests", Guid.NewGuid().ToString("n"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
