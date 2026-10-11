using System.Text.Json;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Docker's command line, for what only it answers — BuildKit's build records — and the label that says whose an object is.</summary>
/// <remarks>
/// Everything the Engine itself knows is asked of it over its socket instead (<see cref="DockerSocket"/>). Docker is
/// looked at only while its daemon already runs: starting a virtual machine of gigabytes to ask what is in it is the
/// opposite of cleaning up.
/// </remarks>
public sealed class Docker(IPrograms programs)
{
    /// <summary>The label a container, a network, a volume or an image carries to say whose it is.</summary>
    public const string OwnerLabel = "snail.owner";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Runs a docker command.</summary>
    public Task<ProgramRun> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        programs.RunAsync("docker", arguments, Patience, cancellationToken);

    /// <summary>Reads with a docker command, sharing the answer across one census.</summary>
    public Task<ProgramRun> ReadAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        programs.ReadAsync("docker", arguments, IPrograms.Census, Patience, cancellationToken);

    /// <summary>The objects of a <c>--format '{{json .}}'</c> listing, one JSON object a line.</summary>
    public static IReadOnlyList<JsonElement> Lines(string output)
    {
        var lines = new List<JsonElement>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);

                lines.Add(document.RootElement.Clone());
            }
            catch (JsonException)
            {
            }
        }

        return lines;
    }

    /// <summary>A text field of a listed object; empty when it has none.</summary>
    public static string Text(JsonElement line, string field) =>
        line.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>The owner a set of labels names; nobody when it names none.</summary>
    public static Owner OwnerOf(IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue(OwnerLabel, out var owner) ? Owner.Parse(owner) : Owner.Nobody;
}
