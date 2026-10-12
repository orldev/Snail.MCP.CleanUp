namespace Snail.MCP.CleanUp.Application.Ports;

/// <summary>Duties one server of the machine does for all of them, held in shifts that run out unless renewed.</summary>
public interface IShifts
{
    /// <summary>Takes the duty, or keeps it, for this long; false when another watcher's shift has not run out.</summary>
    Task<bool> TryTakeAsync(string duty, string watcher, TimeSpan length, CancellationToken cancellationToken);

    /// <summary>Gives the duty up, so that another watcher takes it at once rather than when the shift runs out.</summary>
    Task LeaveAsync(string duty, string watcher, CancellationToken cancellationToken);
}
