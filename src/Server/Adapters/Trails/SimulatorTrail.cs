namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The simulators of this Mac by UDID, so that one an agent made under a name of its own is still noticed.</summary>
/// <remarks>
/// An agent that runs <c>simctl create my-phone</c> itself makes a simulator nothing names as its own. The
/// trail sees it appear during the session and, unclaimed, it becomes a discovery: reported with its
/// session, never removed on the strength of having appeared.
/// </remarks>
public sealed class SimulatorTrail(SimulatorDevices simulators) : ITrail
{
    /// <inheritdoc/>
    public string Name => MachineKinds.Simulator;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var listed = await simulators.ListAsync(cancellationToken);

        return [.. listed.Devices.Select(device => new Trace(Name, device.Udid, device.Name))];
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);
}
