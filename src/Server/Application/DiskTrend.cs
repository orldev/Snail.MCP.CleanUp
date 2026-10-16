using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Application;

/// <summary>How the free space moved over the last day, and what grew while it fell.</summary>
/// <remarks>
/// Free space once fell by 50 GB in a day and nothing said so until it was nearly gone (2026-10-13): the inventory gave the
/// free space of the moment, which looks fine until it is not. Every census now writes the free space down, and the sizes
/// of what grows that this server measures — the scratchpads of sessions, test results, caches — and the inventory says
/// when the free space fell by more than a setting within a day, naming what grew most in that day.
/// </remarks>
public sealed class DiskTrend(IVolume volume, IDiskHistory history, ServerConfig config, TimeProvider clock)
{
    /// <summary>Below this size a thing is not written down as a grower: what takes less cannot have taken gigabytes.</summary>
    public const long SmallestGrower = 100_000_000;

    /// <summary>How many of the largest growers a census writes down.</summary>
    public const int MostGrowers = 30;

    /// <summary>How many growers a warning names.</summary>
    public const int GrowersNamed = 5;

    /// <summary>The span a fall is measured over.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromDays(1);

    /// <summary>Writes down the free space of this moment and the sizes of the largest growers of an inventory; false when it could not.</summary>
    /// <remarks>A full disk is when the history matters most and can least be written: a census goes on without it.</remarks>
    public async Task<bool> TryRecordAsync(Inventory inventory, CancellationToken cancellationToken)
    {
        if (volume.Measure() is not { } measured)
            return false;

        var now = clock.GetUtcNow();
        var largest = inventory.Resources
            .Where(resource => IsGrower(resource) && resource.Bytes >= SmallestGrower)
            .OrderByDescending(resource => resource.Bytes)
            .Take(MostGrowers);
        var growers = largest.Select(resource => new SizeSample(now, resource.Id, resource.Title, resource.Bytes!.Value)).ToList();

        try
        {
            await history.RecordAsync(new DiskSample(now, measured.Free, measured.Total), growers, cancellationToken);

            return true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The disk section of an inventory: the free space now, how far it fell within a day, and a warning naming the growers when it fell too far.</summary>
    public async Task<JsonObject> ReportAsync(CancellationToken cancellationToken)
    {
        if (volume.Measure() is not { } measured)
            return new JsonObject { ["note"] = "the volume home lives on could not be read" };

        var report = Disk.Report(measured.Free, measured.Total);
        var since = clock.GetUtcNow() - Window;
        var samples = await history.FreeSinceAsync(since, cancellationToken);

        if (samples.Count == 0)
            return report;

        var fell = samples.Max(sample => sample.Free) - measured.Free;

        if (fell <= 0)
            return report;

        report["fellWithinADay"] = Views.Size(fell);

        if (fell < Warnings.Bytes(config.DiskFallWarningGigabytes))
            return report;

        var growers = Growers(await history.SizesSinceAsync(since, cancellationToken));
        var named = growers.Count > 0
            ? $"what grew most: {string.Join("; ", growers.Select(grower => $"{grower.Title} +{Views.Size(grower.Grew)}"))}"
            : "nothing this server measures grew by much: what filled it lies outside what it looks at";

        report["trend"] = $"free space fell by {Views.Size(fell)} within a day; {named}";
        report["growers"] = growers.Count > 0
            ? new JsonArray([.. growers.Select(grower => (JsonNode)new JsonObject { ["id"] = grower.Id, ["title"] = grower.Title, ["grew"] = Views.Size(grower.Grew), ["size"] = Views.Size(grower.Size) })])
            : null;

        return Views.Compact(report);
    }

    /// <summary>What a census writes down the size of: the scratchpads of sessions, test results, and caches.</summary>
    private static bool IsGrower(Resource resource) =>
        resource.Facts.ContainsKey(FileKind.SessionFact) || string.Equals(resource.Kind, MachineKinds.TestResults, StringComparison.Ordinal) || resource.IsShared;

    /// <summary>What grew most within the samples: each thing's last size less its first, the largest growths first.</summary>
    private static List<(string Id, string Title, long Grew, long Size)> Growers(IReadOnlyList<SizeSample> samples)
    {
        var first = new Dictionary<string, SizeSample>(StringComparer.Ordinal);
        var last = new Dictionary<string, SizeSample>(StringComparer.Ordinal);

        foreach (var sample in samples)
        {
            first.TryAdd(sample.ResourceId, sample);
            last[sample.ResourceId] = sample;
        }

        var grown = last.Values
            .Select(sample => (Id: sample.ResourceId, sample.Title, Grew: sample.Bytes - first[sample.ResourceId].Bytes, Size: sample.Bytes))
            .Where(grower => grower.Grew > 0);

        return [.. grown.OrderByDescending(grower => grower.Grew).Take(GrowersNamed)];
    }
}
