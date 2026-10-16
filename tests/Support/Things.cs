using Snail.MCP.CleanUp.Domain;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>Resources and sessions written by hand, the way a test describes a machine.</summary>
internal static class Things
{
    public static readonly Owner Me = Owner.Session("me");

    public static readonly Owner Neighbour = Owner.Session("neighbour");

    public static readonly Owner Gone = Owner.Session("gone");

    public static Resource Of(string kind, string key, Owner? owner = null, Evidence evidence = Evidence.Ledger) => new()
    {
        Kind = kind,
        Key = key,
        Title = $"{kind} {key}",
        Owner = owner ?? Owner.Nobody,
        Evidence = evidence
    };

    public static Session Alive(Owner owner) => new(owner, 1, IsAlive: true) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    public static Session Dead(Owner owner) => new(owner, 2, IsAlive: false) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    public static Inventory Inventory(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, string>? kept = null,
        IReadOnlyList<Discovery>? discoveries = null) =>
        new(DateTimeOffset.UnixEpoch, resources, [Alive(Me), Alive(Neighbour), Dead(Gone)])
        {
            Asker = Me,
            Kept = kept ?? new Dictionary<string, string>(),
            Discoveries = discoveries ?? []
        };
}
