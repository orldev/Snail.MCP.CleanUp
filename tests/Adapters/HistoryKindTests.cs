using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>The transcripts of 2026-11-02: 123 sessions of 11.5 GB in <c>~/.claude/projects</c>, the oldest from 2026-08-02.</summary>
public sealed class HistoryKindTests : IDisposable
{
    private const string Session = "33990218-3c4e-433d-a3c6-be9f867ddba3";

    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    public void Dispose() => _scratch.Dispose();

    private ServerConfig Config => new() { ClaudeProjects = _scratch.File("projects"), DataDirectory = _scratch.File("data") };

    private HistoryKind Kind => new(Config, new Sizes(_clock), _clock);

    /// <summary>A transcript with a folder of subagents beside it, last written that many days ago.</summary>
    private string Transcript(string session, int daysAgo, string project = "-Users-orldev-Projects-claude")
    {
        var folder = Directory.CreateDirectory(Path.Combine(Config.ClaudeProjects, project, session, "subagents")).FullName;
        var transcript = Path.Combine(Config.ClaudeProjects, project, $"{session}.jsonl");
        var written = _clock.GetUtcNow().UtcDateTime.AddDays(-daysAgo);

        File.WriteAllText(transcript, new string('x', 4096));
        File.WriteAllText(Path.Combine(folder, "agent-1.jsonl"), new string('y', 4096));
        File.SetLastWriteTimeUtc(transcript, written);
        Directory.SetLastWriteTimeUtc(folder, written);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(folder)!, written);

        return transcript;
    }

    [Fact]
    public async Task Transcript_NobodyWroteIntoForItsDays_IsItsSessionsWithTheFolderBesideIt()
    {
        var transcript = Transcript(Session, daysAgo: 92);

        var found = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(MachineKinds.History, found.Kind);
        Assert.Equal(transcript, found.Key);
        Assert.Equal(Owner.Session(Session), found.Owner);
        Assert.Equal(Evidence.Label, found.Evidence);
        Assert.False(found.IsShared);
        Assert.True(found.Bytes >= 2 * 4096);
    }

    [Fact]
    public async Task Transcript_WrittenWithinItsDays_IsNotOffered()
    {
        Transcript(Session, daysAgo: 2);

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }

    [Fact]
    public async Task Transcript_OfARunningSession_IsNotOffered()
    {
        Transcript(Session, daysAgo: 92);

        Session running = new(Owner.Session(Session), 1, IsAlive: true);

        Assert.Empty((await Kind.ObserveAsync([running], CancellationToken.None)).Resources);
    }

    /// <summary>The project folder keeps whatever else lies in it, such as a memory folder.</summary>
    [Fact]
    public async Task Release_ErasesTheTranscriptAndItsFolder_AndLeavesTheRestOfTheProject()
    {
        var transcript = Transcript(Session, daysAgo: 92);
        var memory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(transcript)!, "memory")).FullName;
        var found = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);

        var release = await Kind.ReleaseAsync(found, CancellationToken.None);

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(File.Exists(transcript));
        Assert.False(Directory.Exists(transcript[..^".jsonl".Length]));
        Assert.True(Directory.Exists(memory));
    }

    [Fact]
    public async Task Release_OfASessionResumedSinceThePlan_LeavesIt()
    {
        var transcript = Transcript(Session, daysAgo: 92);
        var found = Assert.Single((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
        File.SetLastWriteTimeUtc(transcript, _clock.GetUtcNow().UtcDateTime);

        var release = await Kind.ReleaseAsync(found, CancellationToken.None);

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.True(File.Exists(transcript));
    }

    [Fact]
    public async Task Files_ThatAreNoSessionsTranscript_AreNotOffered()
    {
        var project = Directory.CreateDirectory(Path.Combine(Config.ClaudeProjects, "-Users-orldev")).FullName;
        File.WriteAllText(Path.Combine(project, "notes.jsonl"), "{}");
        File.SetLastWriteTimeUtc(Path.Combine(project, "notes.jsonl"), _clock.GetUtcNow().UtcDateTime.AddDays(-92));

        Assert.Empty((await Kind.ObserveAsync([], CancellationToken.None)).Resources);
    }
}
