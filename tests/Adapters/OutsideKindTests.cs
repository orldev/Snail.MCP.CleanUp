using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>The places of 2026-10-13 under a home and a temporary folder of the test's own.</summary>
public sealed class OutsideKindTests : IDisposable
{
    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private OutsideKind Kind => new(new Sizes(new FakeTimeProvider(DateTimeOffset.UtcNow)), OutsideKind.Known(_scratch.File("home"), _scratch.File("T")));

    private string Filled(string relative, int bytes)
    {
        var path = _scratch.File(relative);

        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "blob"), new byte[bytes]);

        return path;
    }

    [Fact]
    public async Task OnnxFolder_AndNuGetScratch_AreReportedCachesWithTheirSize_NeverRemoved()
    {
        var onnx = Filled("home/Library/Application Support/Microsoft/DeveloperTools/.onnxruntime", 50_000);
        var scratch = Filled("T/NuGetScratch", 20_000);

        var observed = await Kind.ObserveAsync([], CancellationToken.None);
        var plan = Planner.Decide(Inventory([.. observed.Resources]), Scope.Caches, "plan");

        Assert.Equal([onnx, scratch], observed.Resources.Select(resource => resource.Key));
        Assert.All(observed.Resources, resource => Assert.True(resource.IsShared && !resource.IsRegenerable && resource.Bytes >= 20_000));
        Assert.Contains(observed.Claimed, id => id == $"file:{scratch}");
        Assert.All(plan.Decisions, decision => Assert.Equal(Verdict.Report, decision.Verdict));
        Assert.Contains("ONNX Runtime", plan.Decisions.Single(decision => decision.Resource.Key == onnx).Reason, StringComparison.Ordinal);
        Assert.Contains("restore under way", plan.Decisions.Single(decision => decision.Resource.Key == scratch).Reason, StringComparison.Ordinal);
        Assert.Empty(Planner.Decide(Inventory([.. observed.Resources]), Scope.Everything, "plan").Removals);
    }

    [Fact]
    public async Task Place_ThatIsMissingOrEmpty_IsNotListed()
    {
        Directory.CreateDirectory(_scratch.File("T/NuGetScratch"));

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task Release_IsRefused_AndTouchesNothing()
    {
        var scratch = Filled("T/NuGetScratch", 20_000);
        var resource = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(Outcome.Failed, (await Kind.ReleaseAsync(resource, CancellationToken.None)).Outcome);
        Assert.True(File.Exists(Path.Combine(scratch, "blob")));
    }
}
