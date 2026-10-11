using System.Globalization;
using System.Text.Json;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Docker as its command line shows it: objects as JSON lines, labels as <c>key=value</c> pairs.</summary>
/// <remarks>
/// Docker is looked at only while its daemon already runs. A Mac with Docker Desktop or OrbStack stopped is
/// an ordinary Mac, and starting a virtual machine of gigabytes to ask what is in it is the opposite of
/// cleaning up.
/// </remarks>
public sealed class Docker(IPrograms programs)
{
    /// <summary>The label a container, a network, a volume or an image carries to say whose it is.</summary>
    public const string OwnerLabel = "snail.owner";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Whether the daemon answers; null when it does, the reason when it does not.</summary>
    public async Task<string?> UnreachableAsync(CancellationToken cancellationToken)
    {
        var run = await programs.RunAsync("docker", ["info", "--format", "{{.ServerVersion}}"], TimeSpan.FromSeconds(10), cancellationToken);

        if (run.IsMissing)
            return "docker is not installed";

        return run.IsSuccess ? null : "docker's daemon is not running, so nothing of Docker could be seen";
    }

    /// <summary>Runs a docker command.</summary>
    public Task<ProgramRun> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        programs.RunAsync("docker", arguments, Patience, cancellationToken);

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

    /// <summary>The labels of a listed object, written as Docker writes them: <c>a=1,b=2</c>.</summary>
    public static IReadOnlyDictionary<string, string> Labels(string written) =>
        written.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);

    /// <summary>The owner a set of labels names; nobody when it names none.</summary>
    public static Owner OwnerOf(IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue(OwnerLabel, out var owner) ? Owner.Parse(owner) : Owner.Nobody;

    /// <summary>A size as Docker prints it, such as <c>1.21GB</c> or <c>512.3kB</c>; null when it is not one.</summary>
    public static long? Bytes(string written)
    {
        var text = written.Split(' ')[0].Trim();
        var unit = new string([.. text.SkipWhile(letter => char.IsDigit(letter) || letter == '.')]);
        var number = text[..(text.Length - unit.Length)];
        double factor = unit.ToUpperInvariant() switch
        {
            "B" => 1,
            "KB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "TB" => 1e12,
            _ => 0
        };

        return factor > 0 && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? (long)(value * factor) : null;
    }
}
