using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Domain;
using Snail.MCP.CleanUp.Tools.Prompts;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Application;

public sealed class RunsTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    public RunsTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    private Runs RunsFor(Owner owner) => new(
        new ServerConfig { DataDirectory = _scratch.Path },
        new FakeSessions(owner),
        new SqliteLedger(_file),
        new Programs(new Performance(TimeProvider.System), TimeProvider.System),
        new FakeProcessTable(),
        new FakeTimeProvider(DateTimeOffset.UtcNow));

    /// <summary>A program started for no session the server can tell is on the record as nobody's, which no cleanup removes: the answer says so.</summary>
    [Fact]
    public async Task Start_ForNoSessionItCanTell_SaysTheProgramIsNobodys()
    {
        var answer = await RunsFor(Owner.Nobody).StartAsync("sleep 1", _scratch.Path, CancellationToken.None);

        await EndedAsync([answer]);

        Assert.True(answer.IsSuccess);
        Assert.Equal(Messages.RunOfNobody, answer.Data!["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task Start_ForItsSession_HasNoNote()
    {
        var answer = await RunsFor(Owner.Session("me")).StartAsync("sleep 1", _scratch.Path, CancellationToken.None);

        await EndedAsync([answer]);

        Assert.Null(answer.Data!["note"]);
    }

    /// <summary>
    /// The key of what was started is read from the program after it starts, and a command that fails at once may be gone
    /// by then: half of twenty runs of <c>exit 3</c> threw (2026-10-16). Each is answered now, and one that ended first says so.
    /// </summary>
    [Fact]
    public async Task Start_CommandThatEndsAtOnce_IsAnsweredNotThrown()
    {
        var runs = RunsFor(Owner.Session("me"));
        var answers = new List<Answer>();

        for (var attempt = 0; attempt < 20; attempt++)
            answers.Add(await runs.StartAsync("exit 3", _scratch.Path, CancellationToken.None));

        await EndedAsync(answers);

        Assert.All(answers, answer => Assert.True(answer.IsSuccess, answer.Error));
        Assert.All(answers.Where(answer => answer.Data!["id"] is null), answer => Assert.Equal(Messages.RunEndedAtOnce, answer.Data!["note"]!.GetValue<string>()));
    }

    /// <summary>Waits for the programs the answers started, so that the test's folder is not removed under a shell still writing its log.</summary>
    private static async Task EndedAsync(IEnumerable<Answer> answers)
    {
        foreach (var pid in answers.Select(answer => answer.Data!["pid"]!.GetValue<int>()))
        {
            for (var wait = 0; wait < 100 && IsRunning(pid); wait++)
                await Task.Delay(20);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);

            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
