using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>Docker's Engine API over its unix socket: an HTTP request where the command line started a process.</summary>
/// <remarks>
/// A census used to start <c>docker</c> twenty times — <c>docker info</c> alone eight times, once per kind — and each
/// start costs a process and the CLI's own warm-up. The Engine answers the same questions as JSON over the socket the
/// CLI talks to anyway. The socket is looked for where the usual engines put it; when none is there, Docker is not
/// running, and nothing is started to find out.
/// <para>
/// The places are looked at again whenever the Engine is asked whether it answers, and the first that does is used: a
/// server started before Docker Desktop saw no Docker until it was started again, and the socket a stopped engine left
/// behind hid one that ran elsewhere (2026-10-16).
/// </para>
/// </remarks>
public sealed class DockerSocket : IDockerEngine, IDisposable
{
    private static readonly TimeSpan Freshness = IPrograms.Census;

    private readonly Func<IReadOnlyList<string>> _places;

    private readonly TimeProvider _clock;

    private readonly Lock _sharing = new();

    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    private string? _answering;

    private (DateTimeOffset At, Task<DockerSnapshot?> Snapshot)? _last;

    /// <summary>The Engine at the first socket that answers of those where engines put theirs.</summary>
    public DockerSocket(TimeProvider clock)
        : this(clock, () => [.. Places.Where(File.Exists)])
    {
    }

    /// <summary>The Engine at this socket; none when it is null.</summary>
    public DockerSocket(string? socket, TimeProvider clock)
        : this(clock, () => socket is null ? [] : [socket])
    {
    }

    /// <summary>The Engine at the first socket of those the places name now that answers.</summary>
    internal DockerSocket(TimeProvider clock, Func<IReadOnlyList<string>> places)
    {
        _clock = clock;
        _places = places;
    }

    /// <summary>Where Docker Desktop, OrbStack, Colima and Rancher Desktop put their sockets, after DOCKER_HOST.</summary>
    public static IReadOnlyList<string> Places
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var named = Environment.GetEnvironmentVariable("DOCKER_HOST") is { } host && host.StartsWith("unix://", StringComparison.Ordinal) ? [host[7..]] : Array.Empty<string>();

            return [.. named, $"{home}/.orbstack/run/docker.sock", $"{home}/.docker/run/docker.sock", "/var/run/docker.sock", $"{home}/.colima/default/docker.sock", $"{home}/.rd/docker.sock"];
        }
    }

    /// <inheritdoc/>
    public async Task<string?> UnreachableAsync(CancellationToken cancellationToken)
    {
        var places = _places();

        if (places.Count == 0)
            return "Docker's daemon is not running: no Engine socket was found, so nothing of Docker could be seen";

        string? first = null;

        foreach (var place in places)
        {
            var said = await PingAsync(place, cancellationToken);

            if (said is null)
            {
                Volatile.Write(ref _answering, place);

                return null;
            }

            first ??= said;
        }

        return first;
    }

    /// <summary>Why the Engine at a socket does not answer; null when it does.</summary>
    private async Task<string?> PingAsync(string socket, CancellationToken cancellationToken)
    {
        try
        {
            using var answer = await Client(socket).GetAsync("_ping", cancellationToken);

            return answer.IsSuccessStatusCode ? null : $"Docker's Engine answered {(int)answer.StatusCode} at {socket}";
        }
        catch (Exception refused) when (refused is HttpRequestException or SocketException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return $"Docker's daemon is not running: nothing answers at {socket}";
        }
    }

    /// <summary>The client of the socket that answered last, or of the first place when none has yet; none when there is no place.</summary>
    private HttpClient? Answering()
    {
        var places = _places();
        var socket = Volatile.Read(ref _answering) ?? (places.Count > 0 ? places[0] : null);

        return socket is null ? null : Client(socket);
    }

    /// <summary>One client per socket for as long as this lives: a client a request may still be using is never disposed under it.</summary>
    private HttpClient Client(string socket)
    {
        lock (_sharing)
        {
            if (_clients.TryGetValue(socket, out var known))
                return known;

            var client = Connected(socket);

            _clients[socket] = client;

            return client;
        }
    }

    private static HttpClient Connected(string socket)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

                try
                {
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), cancellationToken);

                    return new NetworkStream(connection, ownsSocket: true);
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <inheritdoc/>
    public Task<DockerSnapshot?> SnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_sharing)
        {
            var now = _clock.GetUtcNow();

            if (_last is not { } last || now - last.At > Freshness)
                _last = (now, TakenAsync());

            return _last.Value.Snapshot.WaitAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<DockerContainer?> ContainerAsync(string id, CancellationToken cancellationToken)
    {
        using var document = await ReadAsync($"containers/json?all=1&filters={Uri.EscapeDataString($$"""{"id":["{{id}}"]}""")}", cancellationToken);

        return document?.RootElement.EnumerateArray().Select(Container).FirstOrDefault();
    }

    /// <inheritdoc/>
    public Task<DockerAnswer> RemoveAsync(string collection, string id, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, collection == "containers" ? $"containers/{Uri.EscapeDataString(id)}?force=1" : $"{collection}/{Uri.EscapeDataString(id)}", cancellationToken);

    /// <inheritdoc/>
    public Task<DockerAnswer> PruneAsync(string what, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, what == "build" ? "build/prune" : $"images/prune?filters={Uri.EscapeDataString("""{"dangling":["true"]}""")}", cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sharing)
        {
            foreach (var client in _clients.Values)
                client.Dispose();

            _clients.Clear();
        }
    }

    private async Task<DockerSnapshot?> TakenAsync()
    {
        if (await UnreachableAsync(CancellationToken.None) is not null)
            return null;

        var containers = ReadAsync("containers/json?all=1", CancellationToken.None);
        var networks = ReadAsync("networks", CancellationToken.None);
        var volumes = ReadAsync("volumes", CancellationToken.None);
        var labelled = ReadAsync($"images/json?filters={Uri.EscapeDataString($$"""{"label":["{{Docker.OwnerLabel}}"]}""")}", CancellationToken.None);
        var dangling = ReadAsync($"images/json?filters={Uri.EscapeDataString("""{"dangling":["true"]}""")}", CancellationToken.None);
        var usage = ReadAsync("system/df?type=build-cache", CancellationToken.None);

        using var containersRead = await containers;
        using var networksRead = await networks;
        using var volumesRead = await volumes;
        using var labelledRead = await labelled;
        using var danglingRead = await dangling;
        using var usageRead = await usage;

        return new DockerSnapshot(
            List(containersRead, Container),
            List(networksRead, element => new DockerNetwork(Text(element, "Id"), Text(element, "Name"), Labels(element))),
            volumesRead?.RootElement.TryGetProperty("Volumes", out var listed) == true && listed.ValueKind == JsonValueKind.Array
                ? [.. listed.EnumerateArray().Select(element => new DockerVolume(Text(element, "Name"), Labels(element)))]
                : [],
            List(labelledRead, Image),
            List(danglingRead, Image),
            Reclaimable(usageRead));
    }

    private static DockerContainer Container(JsonElement element) => new(
        Text(element, "Id"),
        element.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0 ? names[0].GetString()!.TrimStart('/') : string.Empty,
        Text(element, "Image"),
        Text(element, "State"),
        Labels(element),
        element.TryGetProperty("NetworkSettings", out var settings) && settings.TryGetProperty("Networks", out var networks) && networks.ValueKind == JsonValueKind.Object
            ? [.. networks.EnumerateObject().Select(network => network.Name)]
            : [],
        element.TryGetProperty("Mounts", out var mounts) && mounts.ValueKind == JsonValueKind.Array
            ? [.. mounts.EnumerateArray().Where(mount => Text(mount, "Type") == "volume").Select(mount => Text(mount, "Name"))]
            : []);

    private static DockerImage Image(JsonElement element) => new(
        Text(element, "Id"),
        element.TryGetProperty("RepoTags", out var tags) && tags.ValueKind == JsonValueKind.Array ? [.. tags.EnumerateArray().Select(tag => tag.GetString()!)] : [],
        element.TryGetProperty("Size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
        Labels(element));

    /// <summary>Bytes of build cache no build is using, the way <c>docker system df</c> counts them as reclaimable.</summary>
    private static long Reclaimable(JsonDocument? usage)
    {
        if (usage?.RootElement.TryGetProperty("BuildCache", out var records) != true || records.ValueKind != JsonValueKind.Array)
            return 0;

        return records.EnumerateArray()
            .Where(record => !(record.TryGetProperty("InUse", out var used) && used.GetBoolean()) && !(record.TryGetProperty("Shared", out var shared) && shared.GetBoolean()))
            .Sum(record => record.TryGetProperty("Size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0);
    }

    private static IReadOnlyList<T> List<T>(JsonDocument? document, Func<JsonElement, T> shaped) =>
        document?.RootElement.ValueKind == JsonValueKind.Array ? [.. document.RootElement.EnumerateArray().Select(shaped)] : [];

    private static IReadOnlyDictionary<string, string> Labels(JsonElement element) =>
        element.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object
            ? labels.EnumerateObject().ToDictionary(label => label.Name, label => label.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private async Task<JsonDocument?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (Answering() is not { } http)
            return null;

        using var answer = await http.GetAsync(path, cancellationToken);

        if (!answer.IsSuccessStatusCode)
            return null;

        await using var body = await answer.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private async Task<DockerAnswer> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (Answering() is not { } http)
            return new DockerAnswer(false, false, "Docker's daemon is not running");

        using var request = new HttpRequestMessage(method, path);
        using var answer = await http.SendAsync(request, cancellationToken);
        var said = await answer.Content.ReadAsStringAsync(cancellationToken);

        return new DockerAnswer(answer.IsSuccessStatusCode, answer.StatusCode == HttpStatusCode.NotFound, Said(said, answer.StatusCode));
    }

    private static string Said(string body, HttpStatusCode status)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? $"{(int)status}" : $"{(int)status}";
        }
        catch (JsonException)
        {
            return $"{(int)status}";
        }
    }
}
