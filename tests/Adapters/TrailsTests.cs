using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class TrailsTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private string Made(string relative)
    {
        var path = _scratch.File(relative);

        Directory.CreateDirectory(path);

        return path;
    }

    [Fact]
    public async Task FileTrail_LooksOnlyAsDeepAsItWasTold()
    {
        Made("a/b/c");

        var traces = await new FileTrail([(_scratch.Path, 2)]).FollowAsync([], CancellationToken.None);

        Assert.Equal([_scratch.File("a"), _scratch.File("a/b")], traces.Select(trace => trace.Key).Order(StringComparer.Ordinal));
    }

    /// <summary>A rule learned for something three levels down is useless if the trail never goes there.</summary>
    [Fact]
    public async Task FileTrail_LooksWhereverARulePoints()
    {
        Made("app/Cache/11");

        var rule = new Rule("file", $"{_scratch.Path}/app/Cache/*", RuleAction.Cache);
        var traces = await new FileTrail([(_scratch.Path, 1)]).FollowAsync([rule], CancellationToken.None);

        Assert.Contains(traces, trace => trace.Key == _scratch.File("app/Cache/11"));
    }

    [Fact]
    public async Task FileTrail_MeasuresWhatItIsAskedTo()
    {
        var path = Made("big");

        await File.WriteAllBytesAsync(Path.Combine(path, "blob"), new byte[4096]);

        Assert.Equal(4096, (await new FileTrail([]).MeasureAsync(new Trace("file", path, path), CancellationToken.None)).Bytes);
    }

    [Fact]
    public void PortTrail_ReadsEveryListeningPortOnce_WithItsProgram()
    {
        var traces = PortTrail.Parse("p970\ncSnail.SleepOf\nn*:47321\np12836\ncnode\nn127.0.0.1:3000\nn[::1]:3000\n");

        Assert.Equal(["47321", "3000"], traces.Select(trace => trace.Key));
        Assert.Contains("node (12836)", traces[1].Title, StringComparison.Ordinal);
    }

    /// <summary>Every app a person opens has launchd for a parent too; only programs living elsewhere are followed.</summary>
    [Fact]
    public async Task ProcessTrail_FollowsDetachedPrograms_NotApplicationsOrChildren()
    {
        var processes = new FakeProcessTable();

        processes.Add(10, 1, "/Applications/Safari.app/Contents/MacOS/Safari");
        processes.Add(11, 1, "/usr/local/bin/node server.js");
        processes.Add(12, 500, "/usr/local/bin/node child.js");

        var traces = await new ProcessTrail(processes).FollowAsync([], CancellationToken.None);

        Assert.Equal("/usr/local/bin/node server.js", Assert.Single(traces).Title);
    }

    /// <summary>The learned rules are a file a person reads and edits, so they are written the way a person would write them.</summary>
    [Fact]
    public async Task LearnedRules_AreWrittenAsReadableJson_AfterTheShippedOnes()
    {
        var shipped = new Rule("file", "~/.claude/**", RuleAction.Ignore);
        var rules = new JsonRules(_scratch.File("rules.json"), [shipped]);
        var learned = new Rule("file", "~/Library/Caches/tool/*", RuleAction.Cache) { Note = "rebuilt on next run" };

        await rules.AddAsync(learned, CancellationToken.None);
        await rules.AddAsync(learned with { Note = "rewritten" }, CancellationToken.None);

        var text = await File.ReadAllTextAsync(_scratch.File("rules.json"));

        Assert.Equal([shipped, learned with { Note = "rewritten" }], await rules.ListAsync(CancellationToken.None));
        Assert.Contains("\"action\": \"cache\"", text, StringComparison.Ordinal);
        Assert.Contains("\"pattern\": \"~/Library/Caches/tool/*\"", text, StringComparison.Ordinal);
    }

    public void Dispose() => _scratch.Dispose();
}
