namespace Snail.MCP.CleanUp.Adapters.Trails;

/// <summary>The certificates in the user's keychains, by their SHA-1, so that one an agent added is noticed.</summary>
/// <remarks>
/// A development certificate authority — <c>mkcert -install</c>, <c>dotnet dev-certs --trust</c> — makes every
/// certificate it signs trusted on this Mac. Left behind, it is the most dangerous leftover there is and the
/// least visible one, so it is never removed by this server: it is written down for a person to look at.
/// </remarks>
public sealed class CertificateTrail(IPrograms programs) : ITrail
{
    /// <inheritdoc/>
    public string Name => "certificate";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var run = await programs.ReadAsync("security", ["find-certificate", "-a", "-Z"], IPrograms.Census, TimeSpan.FromSeconds(15), cancellationToken);

        return run.IsSuccess ? Parse(run.Output) : [];
    }

    /// <inheritdoc/>
    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace);

    /// <summary>The certificates of <c>security find-certificate -a -Z</c>: a <c>SHA-1 hash:</c> line starts each, its label is <c>"alis"</c>.</summary>
    internal static IReadOnlyList<Trace> Parse(string output)
    {
        var traces = new Dictionary<string, Trace>(StringComparer.Ordinal);
        string? hash = null;

        foreach (var line in output.Split('\n'))
        {
            var text = line.Trim();

            if (text.StartsWith("SHA-1 hash:", StringComparison.Ordinal))
            {
                hash = text["SHA-1 hash:".Length..].Trim();
                traces.TryAdd(hash, new Trace("certificate", hash, $"certificate {hash}"));
            }
            else if (hash is not null && text.StartsWith("\"alis\"<blob>=\"", StringComparison.Ordinal))
            {
                traces[hash] = traces[hash] with { Title = $"certificate «{text[14..^1]}» ({hash[..8]})" };
            }
        }

        return [.. traces.Values];
    }
}
