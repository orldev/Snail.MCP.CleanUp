using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class DisposalTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private Disposal Disposal => new(
        new ServerConfig { DataDirectory = _scratch.File("data"), TrashDirectory = _scratch.File("Trash") },
        new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero)));

    private string Made(string name)
    {
        var path = _scratch.File(name);

        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "inside.txt"), "work");

        return path;
    }

    /// <summary>A cache moved to the Trash is still gigabytes on the disk; what rebuilds itself is erased instead.</summary>
    [Fact]
    public void Regenerable_IsErased_NotTrashed()
    {
        var path = Made("cache");

        var release = Disposal.Dispose(Of(Kinds.File, path, Me) with { IsRegenerable = true }, path);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(Directory.Exists(path));
        Assert.False(Directory.Exists(_scratch.File("Trash/cache")));
    }

    [Fact]
    public void WhatCannotBeMadeAgain_GoesToTheTrash()
    {
        var path = Made("notes");

        Disposal.Dispose(Of(Kinds.File, path, Me), path);

        Assert.True(File.Exists(_scratch.File("Trash/notes/inside.txt")));
    }

    [Fact]
    public void SecondOfTheSameName_IsNotWrittenOverInTheTrash()
    {
        var first = Made("notes");

        Disposal.Dispose(Of(Kinds.File, first, Me), first);

        var second = Made("notes");
        var release = Disposal.Dispose(Of(Kinds.File, second, Me), second);

        Assert.Contains("notes 2026-10-11", release.Detail, StringComparison.Ordinal);
        Assert.Equal(2, Directory.GetDirectories(_scratch.File("Trash")).Length);
    }

    [Fact]
    public void AlreadyGone_IsSaid() =>
        Assert.Equal(Outcome.Gone, Disposal.Dispose(Of(Kinds.File, "/nowhere", Me), _scratch.File("nowhere")).Outcome);

    public void Dispose() => _scratch.Dispose();
}
