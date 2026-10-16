namespace Snail.MCP.CleanUp.Adapters.Resources;

/// <summary>Places servers are known to write into outside their own data folders: reported with their size, never removed.</summary>
/// <remarks>
/// A server that keeps its data in its own folder is accounted for by that folder; what a library it runs on writes
/// elsewhere is not, and grows unseen. Two such places are known (2026-10-13): ONNX Runtime's folder under Microsoft's
/// developer tools, written by every program that runs a model through it, and NuGet's scratch folder in the temporary
/// folder, written by every restore. Neither is this server's to remove — whose they are and whether they rebuild
/// themselves is their library's to say, and a restore under way works in the scratch folder — so each is a shared
/// cache that is only ever reported, with why. NuGet also writes its configuration on first use; a file of a few bytes is
/// no matter of space, and is not listed.
/// </remarks>
/// <param name="sizes">How the size of a place is counted.</param>
/// <param name="places">The places, each with why it is reported rather than removed.</param>
public sealed class OutsideKind(Sizes sizes, IReadOnlyList<(string Path, string Why)> places) : IResourceKind
{
    /// <summary>The places of this user's Mac.</summary>
    public OutsideKind(Sizes sizes)
        : this(sizes, Known(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath()))
    {
    }

    /// <inheritdoc/>
    public string Name => MachineKinds.Outside;

    /// <summary>The known places under a home and a temporary folder.</summary>
    public static IReadOnlyList<(string Path, string Why)> Known(string home, string temporary) =>
    [
        (
            Path.Combine(home, "Library", "Application Support", "Microsoft", "DeveloperTools", ".onnxruntime"),
            "ONNX Runtime's folder, written by every program that runs a model through it — servers that embed text among them — outside their own data folders; reported so its size is seen, never removed: what it holds and whether it is made again is ONNX Runtime's to say"
        ),
        (
            Path.Combine(Path.TrimEndingDirectorySeparator(temporary), "NuGetScratch"),
            "NuGet's scratch folder, written by every restore — servers that restore packages as they run among them; reported so its size is seen, never removed: a restore under way works in it, and NuGet clears it itself"
        )
    ];

    /// <inheritdoc/>
    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> sessions, CancellationToken cancellationToken)
    {
        var found = new List<Resource>(places.Count);

        foreach (var (path, why) in places)
        {
            if (!Directory.Exists(path) || sizes.Of(path) is not { } size || (size.Bytes == 0 && !size.IsAtLeast))
                continue;

            found.Add(new Resource
            {
                Kind = Name,
                Key = path,
                Title = $"{path}: written outside the data folders of the servers that write it",
                IsShared = true,
                Bytes = size.Bytes,
                IsAtLeast = size.IsAtLeast,
                Blocker = why
            });
        }

        return Task.FromResult(new Observation(Name, found, [.. found.Select(resource => $"file:{resource.Key}")]));
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
        Task.FromResult(Release.Refused(resource, "reported only: this server never removes what other programs write outside their own folders"));
}
