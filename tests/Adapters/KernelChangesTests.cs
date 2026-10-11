using System.Diagnostics;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Native;
using Snail.MCP.CleanUp.Adapters.Trails;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class KernelChangesTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private async Task<KernelChanges> FollowingAsync(params int[] agents)
    {
        var trail = new FileTrail([(_scratch.Path, 1)], new Sizes(TimeProvider.System));

        await trail.FollowAsync([], CancellationToken.None);

        var changes = new KernelChanges(trail);

        changes.Follow(agents);
        changes.Take();

        return changes;
    }

    [Fact]
    public async Task Entry_AddedToAFollowedDirectory_IsWordOfAChange()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var changes = await FollowingAsync();

        Assert.False(changes.Take());

        await File.WriteAllTextAsync(_scratch.File("new"), "x");
        await changes.WaitAsync(Patience, CancellationToken.None);

        Assert.True(changes.Take());
    }

    /// <summary>What happens below a followed directory is not a change of it: only what a census would list is.</summary>
    [Fact]
    public async Task File_WrittenInsideAnEntry_IsNoChange()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var inside = Directory.CreateDirectory(_scratch.File("inside")).FullName;

        using var changes = await FollowingAsync();

        await File.WriteAllTextAsync(Path.Combine(inside, "deep"), "x");
        await changes.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.False(changes.Take());
    }

    [Fact]
    public async Task Agent_ThatEnds_IsWordOfAChange()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var agent = Process.Start(new ProcessStartInfo("/bin/sleep", ["30"]))!;
        using var changes = await FollowingAsync(agent.Id);

        agent.Kill();
        await changes.WaitAsync(Patience, CancellationToken.None);

        Assert.True(changes.Take());
    }

    [Fact]
    public async Task Leaving_StopsFollowingEverything()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var changes = await FollowingAsync(Environment.ProcessId);

        Assert.Equal((1, 1), changes.Followed);

        changes.Leave();

        Assert.Equal((0, 0), changes.Followed);

        await File.WriteAllTextAsync(_scratch.File("unseen"), "x");
        await changes.WaitAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.False(changes.Take());
    }
}
