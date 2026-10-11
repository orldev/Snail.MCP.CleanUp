using System.Globalization;

namespace Snail.MCP.CleanUp.Application;

/// <summary>How the account is shown to a model: compact JSON, sizes in words, and lists cut at a length that fits a context.</summary>
/// <remarks>
/// A machine can hold hundreds of resources, and a tool answer is read into the context whole. Every list is
/// cut at <see cref="MostListed"/> with a count of what was left out, so that a long answer says it is long
/// instead of crowding out the work it was asked for.
/// </remarks>
public static class Views
{
    /// <summary>How many entries one list of an answer shows.</summary>
    public const int MostListed = 20;

    /// <summary>A resource as one line of an answer.</summary>
    public static JsonObject Of(Resource resource) => Compact(new JsonObject
    {
        ["id"] = resource.Id,
        ["title"] = resource.Title,
        ["owner"] = resource.Owner.ToString(),
        ["evidence"] = resource.Evidence.ToString().ToLowerInvariant(),
        ["size"] = Size(resource.Bytes),
        ["blocker"] = resource.Blocker
    });

    /// <summary>A decision of a plan as one line of an answer.</summary>
    public static JsonObject Of(Decision decision) => Compact(new JsonObject
    {
        ["id"] = decision.Resource.Id,
        ["title"] = decision.Resource.Title,
        ["reason"] = decision.Reason,
        ["size"] = Size(decision.Resource.Bytes)
    });

    /// <summary>A discovery as one line of an answer.</summary>
    public static JsonObject Of(Discovery discovery) => Compact(new JsonObject
    {
        ["id"] = discovery.Trace.Id,
        ["title"] = discovery.Trace.Title,
        ["session"] = discovery.Owner.ToString(),
        ["size"] = Size(discovery.Trace.Bytes),
        ["firstSeen"] = discovery.FirstSeen.ToString("u", CultureInfo.InvariantCulture),
        ["isPresent"] = discovery.IsPresent
    });

    /// <summary>A list cut at <see cref="MostListed"/>, saying how many were left out.</summary>
    public static JsonObject Listed<T>(IReadOnlyList<T> items, Func<T, JsonObject> shown) => Compact(new JsonObject
    {
        ["count"] = items.Count,
        ["items"] = items.Count > 0 ? new JsonArray([.. items.Take(MostListed).Select(item => (JsonNode)shown(item))]) : null,
        ["notShown"] = items.Count > MostListed ? items.Count - MostListed : null
    });

    /// <summary>The object without the fields that say nothing: a null read a hundred times is a hundred lines of noise.</summary>
    public static JsonObject Compact(JsonObject shown)
    {
        foreach (var empty in shown.Where(field => field.Value is null).Select(field => field.Key).ToList())
            shown.Remove(empty);

        return shown;
    }

    /// <summary>A size in the unit a person reads it in.</summary>
    public static string? Size(long? bytes) => bytes switch
    {
        null => null,
        < 1_000 => $"{bytes} B",
        < 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e3:0.#} KB"),
        < 1_000_000_000 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e6:0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e9:0.##} GB")
    };
}
