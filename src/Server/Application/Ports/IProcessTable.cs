namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>The programs running on this machine right now.</summary>
public interface IProcessTable
{
    /// <summary>Every process, read in one pass so that parents and children agree with each other.</summary>
    Task<IReadOnlyList<RunningProcess>> ListAsync(CancellationToken cancellationToken);
}
