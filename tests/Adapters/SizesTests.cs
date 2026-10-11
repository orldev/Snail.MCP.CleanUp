using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class SizesTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    public void Dispose() => _scratch.Dispose();

    /// <summary>A file grown deep inside is not seen until the size is too old to believe: that is the price of not counting the tree every census.</summary>
    [Fact]
    public void Directory_WhoseOwnWriteTimeStaysTheSame_IsCountedOnce_UntilItsSizeIsTooOld()
    {
        var deep = Directory.CreateDirectory(_scratch.File("cache/deep")).FullName;
        File.WriteAllBytes(Path.Combine(deep, "a"), new byte[100]);
        var sizes = new Sizes(_clock);

        Assert.Equal(100, sizes.Of(_scratch.File("cache")));

        File.WriteAllBytes(Path.Combine(deep, "b"), new byte[50]);

        Assert.Equal(100, sizes.Of(_scratch.File("cache")));

        _clock.Advance(Sizes.Kept);

        Assert.Equal(150, sizes.Of(_scratch.File("cache")));
    }

    [Fact]
    public void Directory_GivenANewEntryOfItsOwn_IsCountedAgain()
    {
        var cache = Directory.CreateDirectory(_scratch.File("cache")).FullName;
        File.WriteAllBytes(Path.Combine(cache, "a"), new byte[100]);
        var sizes = new Sizes(_clock);

        Assert.Equal(100, sizes.Of(cache));

        File.WriteAllBytes(Path.Combine(cache, "b"), new byte[50]);
        Directory.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(150, sizes.Of(cache));
    }

    [Fact]
    public void Path_ThatIsGone_HasNoSize_AndAFileIsAlwaysItsLength()
    {
        var file = _scratch.File("one");
        File.WriteAllBytes(file, new byte[7]);
        var sizes = new Sizes(_clock);

        Assert.Equal(7, sizes.Of(file));

        File.WriteAllBytes(file, new byte[9]);

        Assert.Equal(9, sizes.Of(file));
        Assert.Null(sizes.Of(_scratch.File("never")));
    }
}
