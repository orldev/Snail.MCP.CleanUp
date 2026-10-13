using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Application;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>
/// Files are written in whole blocks of random bytes, so that the space their blocks take — what macOS counts — and their
/// length — what the listing elsewhere counts — are the same number.
/// </summary>
public sealed class SizesTests : IDisposable
{
    private const int Block = 4096;

    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    public void Dispose() => _scratch.Dispose();

    private void Write(string name, int blocks)
    {
        var path = _scratch.File(name);
        var bytes = new byte[blocks * Block];

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A file grown deep inside is not seen until the size is too old to believe: that is the price of not counting the tree every census.</summary>
    [Fact]
    public void Directory_WhoseOwnWriteTimeStaysTheSame_IsCountedOnce_UntilItsSizeIsTooOld()
    {
        Write("cache/deep/a", 2);
        var sizes = new Sizes(_clock);

        Assert.Equal(new Measured(2 * Block, IsAtLeast: false), sizes.Of(_scratch.File("cache")));

        Write("cache/deep/b", 1);

        Assert.Equal(2 * Block, sizes.Of(_scratch.File("cache"))?.Bytes);

        _clock.Advance(Sizes.Kept);

        Assert.Equal(3 * Block, sizes.Of(_scratch.File("cache"))?.Bytes);
    }

    [Fact]
    public void Directory_GivenANewEntryOfItsOwn_IsCountedAgain()
    {
        Write("cache/a", 2);
        var sizes = new Sizes(_clock);

        Assert.Equal(2 * Block, sizes.Of(_scratch.File("cache"))?.Bytes);

        Write("cache/b", 1);
        Directory.SetLastWriteTimeUtc(_scratch.File("cache"), DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(3 * Block, sizes.Of(_scratch.File("cache"))?.Bytes);
    }

    [Fact]
    public void Path_ThatIsGone_HasNoSize_AndAFileIsAlwaysItsLength()
    {
        var file = _scratch.File("one");
        File.WriteAllBytes(file, new byte[7]);
        var sizes = new Sizes(_clock);

        Assert.Equal(new Measured(7, IsAtLeast: false), sizes.Of(file));

        File.WriteAllBytes(file, new byte[9]);

        Assert.Equal(9, sizes.Of(file)?.Bytes);
        Assert.Null(sizes.Of(_scratch.File("never")));
    }

    /// <summary>
    /// The scratchpad of 2026-10-13: copies of a repository with their build trees, and hang dumps of gigabytes past them. The
    /// count stopped at its ceiling and the sum of the small files it had reached was shown as the size — 3.76 GB of 50.
    /// </summary>
    [Fact]
    public void Count_ThatStopsAtItsCeiling_SaysAtLeast_AndNeverPassesThePartialSumOffAsTheSize()
    {
        for (var index = 0; index < 30; index++)
            Write($"pad/copy/bin/obj/{index}.dll", 1);

        Write("pad/copy/tests/TestResults/run/dotnet_1_hangdump.dmp", 64);
        var stopped = new Sizes(_clock, mostCounted: 10).Of(_scratch.File("pad"));
        var whole = new Sizes(_clock).Of(_scratch.File("pad"));

        Assert.True(stopped?.IsAtLeast);
        Assert.True(stopped?.Bytes < 94 * Block);
        Assert.Equal(new Measured(94 * Block, IsAtLeast: false), whole);
    }

    /// <summary>Hidden folders and deep ones are counted; a link is not followed, the way du counts.</summary>
    [Fact]
    public void Count_GoesIntoHiddenAndDeepFolders_ButNotThroughALink()
    {
        Write($"pad/.git/{string.Join('/', Enumerable.Repeat("deep", 40))}/pack", 3);
        Write("pad/.hidden", 1);
        Write("elsewhere/big", 50);
        Directory.CreateSymbolicLink(_scratch.File("pad/link"), _scratch.File("elsewhere"));

        Assert.Equal(new Measured(4 * Block, IsAtLeast: false), new Sizes(_clock).Of(_scratch.File("pad")));
    }

    [Fact]
    public void Size_CountedShort_IsShownAsAtLeast()
    {
        Assert.Equal("at least 3.76 GB", Views.Size(3_760_000_000, isAtLeast: true));
        Assert.Equal("3.76 GB", Views.Size(3_760_000_000, isAtLeast: false));
        Assert.Null(Views.Size(null, isAtLeast: true));
    }
}
