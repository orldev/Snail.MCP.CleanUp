using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A process table the test writes.</summary>
internal sealed class FakeProcessTable : IProcessTable
{
    public List<RunningProcess> Running { get; } = [];

    public RunningProcess Add(int pid, int parent, string command, DateTimeOffset? started = null, string? executable = null, string? session = null, int? agent = null)
    {
        var process = new RunningProcess(pid, parent, started ?? DateTimeOffset.UnixEpoch.AddDays(1), command) { Executable = executable, SessionId = session, AgentPid = agent };

        Running.Add(process);

        return process;
    }

    public Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunningProcess>>([.. Running]);

    public void Refresh()
    {
    }
}
