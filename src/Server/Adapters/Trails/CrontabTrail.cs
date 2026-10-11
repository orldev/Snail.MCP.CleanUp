namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The lines of the user's crontab: something that will run again tomorrow, whoever wrote it.</summary>
public sealed class CrontabTrail(IPrograms programs) : ITrail
{
    /// <inheritdoc/>
    public string Name => "crontab";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("crontab", ["-l"], TimeSpan.FromSeconds(5), cancellationToken);

        if (!run.IsSuccess)
            return [];

        return
        [
            .. run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith('#'))
                .Distinct(StringComparer.Ordinal)
                .Select(line => new Trace(Name, line, line))
        ];
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);
}
