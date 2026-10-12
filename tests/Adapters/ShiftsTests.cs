using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;

namespace Snail.MCP.CleanUp.Tests.Adapters;

public sealed class ShiftsTests : IDisposable
{
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(1);

    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    public ShiftsTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private SqliteShifts Shifts => new(_file, _clock);

    [Fact]
    public async Task Duty_HeldByOne_IsRefusedToAnother_AndKeptByItsHolder()
    {
        Assert.True(await Shifts.TryTakeAsync("census", "a", Length, CancellationToken.None));
        Assert.False(await Shifts.TryTakeAsync("census", "b", Length, CancellationToken.None));
        Assert.True(await Shifts.TryTakeAsync("census", "a", Length, CancellationToken.None));
        Assert.True(await Shifts.TryTakeAsync("other", "b", Length, CancellationToken.None));
    }

    /// <summary>A server that died holding the watch keeps it only until its shift runs out.</summary>
    [Fact]
    public async Task Shift_NotRenewed_PassesToTheNextWhenItRunsOut()
    {
        await Shifts.TryTakeAsync("census", "a", Length, CancellationToken.None);

        _clock.Advance(Length - TimeSpan.FromSeconds(1));
        Assert.False(await Shifts.TryTakeAsync("census", "b", Length, CancellationToken.None));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await Shifts.TryTakeAsync("census", "b", Length, CancellationToken.None));
        Assert.False(await Shifts.TryTakeAsync("census", "a", Length, CancellationToken.None));
    }

    [Fact]
    public async Task Shift_Left_PassesAtOnce_AndOnlyItsHolderCanLeaveIt()
    {
        await Shifts.TryTakeAsync("census", "a", Length, CancellationToken.None);

        await Shifts.LeaveAsync("census", "b", CancellationToken.None);
        Assert.False(await Shifts.TryTakeAsync("census", "b", Length, CancellationToken.None));

        await Shifts.LeaveAsync("census", "a", CancellationToken.None);
        Assert.True(await Shifts.TryTakeAsync("census", "b", Length, CancellationToken.None));
    }

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
    }
}
