using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>The repositories of 2026-10-28: 238 bin and obj folders of 39.6 GB that agents' builds left and nothing removed.</summary>
public sealed class ProjectOutputsKindTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeProcessTable _table = new();

    private FakeTimeProvider _clock = new(DateTimeOffset.UtcNow.AddDays(10));

    public void Dispose() => _scratch.Dispose();

    private ServerConfig Config => new()
    {
        ScratchRoot = _scratch.File("tmp"),
        DataDirectory = _scratch.File("data"),
        TrashDirectory = _scratch.File("Trash"),
        EditorUserDirectory = _scratch.File("Code/User")
    };

    private ProjectOutputsKind Kind => new(new Repositories(Config), _table, new Disposal(Config, _clock), new Sizes(_clock), _clock);

    private string Repository(string path)
    {
        var repository = _scratch.File(path);

        Directory.CreateDirectory(Path.Combine(repository, ".git"));

        return repository;
    }

    /// <summary>A project with a build in its bin and obj; the folders of both.</summary>
    private static (string Bin, string Obj) Built(string repository, string project, int kilobytes = 64)
    {
        var folder = Directory.CreateDirectory(Path.Combine(repository, "src", project)).FullName;
        var bin = Path.Combine(folder, "bin");
        var obj = Path.Combine(folder, "obj");
        var bytes = new byte[kilobytes * 1024];

        File.WriteAllText(Path.Combine(folder, $"{project}.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Directory.CreateDirectory(Path.Combine(bin, "Release", "net10.0", "osx-arm64"));
        Directory.CreateDirectory(obj);
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(bin, "Release", "net10.0", "osx-arm64", $"{project}.dll"), bytes);
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), "{}");

        return (bin, obj);
    }

    private static Session WorkingIn(string directory) => new(Owner.Session("s1"), 1, IsAlive: false) { StartedAt = DateTimeOffset.UnixEpoch, Directory = directory };

    [Fact]
    public async Task Outputs_NoBuildWroteIntoForAWeek_AreOneCacheOfTheirRepository_WithTheirSize()
    {
        var repository = Repository("Projects/Vertical/Vertical.Hub");
        Built(repository, "Hub");
        Built(repository, "Hub.Tests");

        var outputs = Assert.Single((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        Assert.Equal(MachineKinds.ProjectOutputs, outputs.Kind);
        Assert.Equal(repository, outputs.Key);
        Assert.True(outputs.IsShared);
        Assert.True(outputs.IsRegenerable);
        Assert.Equal("4", outputs.Facts[ProjectOutputsKind.FoldersFact]);
        Assert.True(outputs.Bytes >= 2 * 64 * 1024);
        Assert.Contains("Vertical.Hub", outputs.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_ErasesTheBinAndObj_AndLeavesTheProject()
    {
        var repository = Repository("Projects/Vertical/Vertical.Hub");
        var (bin, obj) = Built(repository, "Hub");
        var outputs = Assert.Single((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);

        var release = await Kind.ReleaseAsync(outputs, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(Directory.Exists(bin));
        Assert.False(Directory.Exists(obj));
        Assert.True(File.Exists(Path.Combine(repository, "src", "Hub", "Hub.csproj")));
    }

    /// <summary>A project built into this week is being worked on: its outputs are what the next build starts from.</summary>
    [Fact]
    public async Task Outputs_OfAProjectBuiltThisWeek_AreNotOffered()
    {
        var repository = Repository("Projects/Vertical/Vertical.Hub");
        Built(repository, "Hub");
        _clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1));

        Assert.Empty((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task Output_ARunningProgramWasStartedFrom_IsLeft()
    {
        var repository = Repository("Projects/Vertical/Vertical.Hub");
        var (bin, _) = Built(repository, "Hub");
        _table.Add(700, 1, $"{bin}/Release/net10.0/osx-arm64/Hub", executable: $"{bin}/Release/net10.0/osx-arm64/Hub");

        var outputs = Assert.Single((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);
        await Kind.ReleaseAsync(outputs, CancellationToken.None);

        Assert.Equal("1", outputs.Facts[ProjectOutputsKind.FoldersFact]);
        Assert.True(Directory.Exists(bin));
    }

    /// <summary>A repository inside another, both worked in: each folder is offered once, by the repository it belongs to.</summary>
    [Fact]
    public async Task Outputs_OfARepositoryInsideAnother_AreOnlyTheInnerOnes()
    {
        var outer = Repository("Projects/Toolkit");
        var inner = Repository("Projects/Toolkit/Snail.Sdk");
        Built(inner, "Mcp");

        var outputs = Assert.Single((await Kind.ObserveAsync([WorkingIn(outer), WorkingIn(inner)], CancellationToken.None)).Resources);

        Assert.Equal(inner, outputs.Key);
        Assert.Equal("2", outputs.Facts[ProjectOutputsKind.FoldersFact]);
    }

    /// <summary>Packages are no build output: installing them again costs a network and minutes, and test results are a kind of their own.</summary>
    [Fact]
    public async Task NodeModulesAndTestResults_AreNotOffered()
    {
        var repository = Repository("Projects/Web/site");
        var modules = Directory.CreateDirectory(Path.Combine(repository, "node_modules", "left-pad")).FullName;
        File.WriteAllText(Path.Combine(repository, "package.json"), "{}");
        File.WriteAllText(Path.Combine(modules, "index.js"), "module.exports = 1;");
        Directory.CreateDirectory(Path.Combine(repository, "tests", "Site.Tests", "TestResults"));

        Assert.Empty((await Kind.ObserveAsync([WorkingIn(repository)], CancellationToken.None)).Resources);
    }
}
