using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class FileKindTests : IDisposable
{
    private const string SessionId = "2647fa9c-8ea1-4ee7-a1f0-7053be900dd8";

    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _table = new();

    private ServerConfig Config => new() { ScratchRoot = _scratch.Path, DataDirectory = _scratch.File("data"), TrashDirectory = _scratch.File("Trash") };

    private FileKind Kind(params Rule[] rules) => new(
        Config,
        new FileTrail([(_scratch.File("apps"), 1)]),
        new JsonRules(_scratch.File("rules.json"), rules),
        _table,
        new Disposal(Config, new FakeTimeProvider(DateTimeOffset.UnixEpoch)),
        _clock);

    private FakeTimeProvider _clock = new(DateTimeOffset.UtcNow.AddDays(1));

    private string Scratchpad()
    {
        var path = _scratch.File($"claude-501/-Users-dev-project/{SessionId}");

        Directory.CreateDirectory(Path.Combine(path, "scratchpad"));

        return path;
    }

    [Fact]
    public async Task Scratchpad_IsItsSessions_ByItsName()
    {
        var path = Scratchpad();

        var scratchpad = Assert.Single((await Kind().ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(path, scratchpad.Key);
        Assert.Equal(Owner.Session(SessionId), scratchpad.Owner);
        Assert.Equal(Evidence.Label, scratchpad.Evidence);
        Assert.Null(scratchpad.Blocker);
    }

    /// <summary>A session SleepOf never heard of goes by another name; its scratchpad, written a minute ago, is not an orphan.</summary>
    [Fact]
    public async Task Scratchpad_WrittenInTheLastHour_IsNotToBeReleased_WhateverTheSessionsSay()
    {
        Scratchpad();
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.NotNull(Assert.Single((await Kind().ObserveAsync([], CancellationToken.None)).Resources).Blocker);
    }

    [Fact]
    public async Task Scratchpad_OfARunningSession_IsNotToBeReleased()
    {
        Scratchpad();

        var running = new Session(SessionId, 1, DateTimeOffset.UnixEpoch, "/work", IsAlive: true);
        var scratchpad = Assert.Single((await Kind().ObserveAsync([running], CancellationToken.None)).Resources);

        Assert.NotNull(scratchpad.Blocker);
    }

    [Fact]
    public async Task Scratchpad_Released_GoesToTheTrash()
    {
        var path = Scratchpad();
        var kind = Kind();

        var release = await kind.ReleaseAsync(Assert.Single((await kind.ObserveAsync([], CancellationToken.None)).Resources), CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(Directory.Exists(path));
        Assert.True(Directory.Exists(_scratch.File($"Trash/{SessionId}")));
    }

    /// <summary>VS Code holds its webview caches open; a cache rule names the program that blocks it.</summary>
    [Fact]
    public async Task CacheRule_MakesSharedRegenerableResources_BlockedWhileTheirProgramRuns()
    {
        Directory.CreateDirectory(_scratch.File("apps/WebStorage/11"));
        _table.Add(9, 1, "/Applications/Visual Studio Code.app/Contents/MacOS/Code", executable: "/Applications/Visual Studio Code.app/Contents/MacOS/Code");

        var rule = new Rule("file", $"{_scratch.Path}/apps/WebStorage/*", RuleAction.Cache) { BlockedWhileRunning = "Code" };
        var cache = Assert.Single((await Kind(rule).ObserveAsync([], CancellationToken.None)).Resources);

        Assert.True(cache.IsShared);
        Assert.True(cache.IsRegenerable);
        Assert.Equal("Code is running and keeps it open", cache.Blocker);
    }

    [Fact]
    public async Task IgnoreRule_MakesNoResource()
    {
        Directory.CreateDirectory(_scratch.File("apps/Noise"));

        Assert.Empty((await Kind(new Rule("file", $"{_scratch.Path}/apps/*", RuleAction.Ignore)).ObserveAsync([], CancellationToken.None)).Resources);
    }

    public void Dispose() => _scratch.Dispose();
}
