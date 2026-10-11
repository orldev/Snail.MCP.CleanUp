using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>A Docker Engine of the test's own: a unix socket that answers HTTP the way the Engine does, from canned JSON.</summary>
internal sealed class FakeEngine : IDisposable
{
    private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

    private readonly CancellationTokenSource _stop = new();

    public FakeEngine()
    {
        _listener.Bind(new UnixDomainSocketEndPoint(Path));
        _listener.Listen();
        _ = ServeAsync();
    }

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snail-{Guid.NewGuid():n}.sock");

    /// <summary>The answer to a request, by its method and its unescaped path: a status and a JSON body.</summary>
    public Func<string, string, (int Status, string Body)> Answer { get; set; } = (_, _) => (404, """{"message":"page not found"}""");

    /// <summary>Every request so far, as <c>METHOD /path?query</c> with the query unescaped.</summary>
    public ConcurrentQueue<string> Requests { get; } = new();

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        _stop.Dispose();
        File.Delete(Path);
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket connection;

            try
            {
                connection = await _listener.AcceptAsync(_stop.Token);
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = AnswerAsync(connection);
        }
    }

    private async Task AnswerAsync(Socket connection)
    {
        await using var stream = new NetworkStream(connection, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var first = await reader.ReadLineAsync();

        if (first is null)
            return;

        var length = 0;

        for (var header = await reader.ReadLineAsync(); !string.IsNullOrEmpty(header); header = await reader.ReadLineAsync())
        {
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (length > 0)
            await reader.ReadBlockAsync(new char[length]);

        var parts = first.Split(' ');
        var path = Uri.UnescapeDataString(parts[1]);
        Requests.Enqueue($"{parts[0]} {path}");

        var (status, body) = Answer(parts[0], path);
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(head);
        await stream.WriteAsync(bytes);
    }
}
