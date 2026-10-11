using System.Diagnostics;
using System.Net.Sockets;

if (args.Length < 2)
{
    await Console.Error.WriteLineAsync("usage: snail-mcp-cleanup-hook <socket> <command> [arguments...]");

    return 2;
}

using var read = new MemoryStream();

await using (var input = Console.OpenStandardInput())
    await input.CopyToAsync(read);

var hook = read.ToArray();

if (await Forwarding.HandedAsync(args[0], hook))
    return 0;

var start = new ProcessStartInfo(args[1], args[2..]) { RedirectStandardInput = true };

using var process = Process.Start(start)!;

await process.StandardInput.BaseStream.WriteAsync(hook);
process.StandardInput.Close();
await process.WaitForExitAsync();

return process.ExitCode;

/// <summary>Handing a hook's event to a running server.</summary>
internal static class Forwarding
{
    /// <summary>How long a server may take to accept: it answers at once or not at all.</summary>
    private static readonly TimeSpan Accepting = TimeSpan.FromSeconds(1);

    /// <summary>How long it may take to handle the event: under the 120 s Claude Code gives a session end.</summary>
    private static readonly TimeSpan Handling = TimeSpan.FromSeconds(110);

    /// <summary>Whether a server took the event and answered; when none did, the caller runs the hook itself.</summary>
    /// <remarks>Any answer counts, a failure too: the server has tried, and doing it a second time would sweep twice.</remarks>
    public static async Task<bool> HandedAsync(string path, byte[] hook)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            using (var accepting = new CancellationTokenSource(Accepting))
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), accepting.Token);

            await socket.SendAsync(hook);
            socket.Shutdown(SocketShutdown.Send);

            using var handling = new CancellationTokenSource(Handling);
            var answer = new byte[512];

            return await socket.ReceiveAsync(answer, SocketFlags.None, handling.Token) > 0;
        }
        catch (Exception unanswered) when (unanswered is SocketException or OperationCanceledException or ArgumentException or IOException)
        {
            return false;
        }
    }
}
