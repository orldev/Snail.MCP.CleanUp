using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>A unix socket a running server takes hook events on, so that a hook does not start a .NET process of its own.</summary>
/// <remarks>
/// <para>
/// Every session start, every stop and every session end runs a hook, and a hook run as <c>dotnet … hook</c> starts the
/// runtime, builds the services and takes a census from cold: 0.57 s at a session start, measured on 2026-10-12. A
/// server is already running beside every session, warm, with its readings shared, so the small native forwarder hands
/// it the event and waits for its answer; only when no server answers does the hook run as a process.
/// </para>
/// <para>
/// The socket lives in a directory only its user may enter. A session end sweeps what the session left, so whoever can
/// write to this socket can end another user's programs — on a shared machine, nobody but the owner may.
/// </para>
/// <para>
/// One server on the machine listens; the others look again every <see cref="Retry"/>, and take the socket over when the
/// one that held it has gone, removing what it left behind.
/// </para>
/// </remarks>
public sealed class HookSocket(ServerConfig config, Hooks hooks, ILogger<HookSocket> logger, TimeProvider clock) : BackgroundService
{
    /// <summary>The longest event a hook may hand over; Claude Code's are a few hundred bytes.</summary>
    private const int Largest = 1 << 16;

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private int _isCounting;

    /// <summary>How often a server that does not hold the socket looks whether it can.</summary>
    public static TimeSpan Retry { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a hook may take to hand over its event and close its side; the forwarder does both at once.</summary>
    /// <remarks>An event is read to its end, so a client that never closed its side held a connection and its task for as long as the server ran.</remarks>
    public static TimeSpan Reading { get; } = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    /// <remarks>It yields first, so that binding the socket is not part of the host's start, which the transport waits for.</remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (Listen() is { } listener)
            {
                using (listener)
                    await ServeAsync(listener, stoppingToken);

                Forget();
            }

            try
            {
                await Task.Delay(Retry, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Binds the socket when no other server answers on it; null when one does or it cannot be made.</summary>
    private Socket? Listen()
    {
        if (OperatingSystem.IsWindows())
            return null;

        var path = config.HookSocket;

        try
        {
            var directory = Path.GetDirectoryName(path)!;

            Directory.CreateDirectory(directory, OwnerOnly);
            File.SetUnixFileMode(directory, OwnerOnly);

            if (File.Exists(path))
            {
                if (IsAnswered(path))
                    return null;

                File.Delete(path);
            }

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(path));
                listener.Listen(16);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

                return listener;
            }
            catch
            {
                listener.Dispose();
                throw;
            }
        }
        catch (Exception refused) when (refused is SocketException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.HooksNotTaken(logger, path, refused.Message);

            return null;
        }
    }

    private static bool IsAnswered(string path)
    {
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            probe.Connect(new UnixDomainSocketEndPoint(path));

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task ServeAsync(Socket listener, CancellationToken stoppingToken)
    {
        Log.TakingHooks(logger, config.HookSocket);

        while (!stoppingToken.IsCancellationRequested)
        {
            Socket connection;

            try
            {
                connection = await listener.AcceptAsync(stoppingToken);
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = AnswerAsync(connection, stoppingToken);
        }
    }

    /// <summary>Reads one event to its end, handles it, and says <c>ok</c> — or why not — before closing.</summary>
    private async Task AnswerAsync(Socket connection, CancellationToken stoppingToken)
    {
        using (connection)
        {
            await using var stream = new NetworkStream(connection, ownsSocket: false);
            var said = "ok";

            try
            {
                var input = await ReadAsync(stream, stoppingToken);

                if (Hooks.Read(input).Name == "Stop")
                    CountInTheBackground();
                else
                    await hooks.HandleAsync(input, stoppingToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Log.HookNotHandled(logger, failure);
                said = $"failed: {failure.Message}";
            }

            try
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes($"{said}\n"), stoppingToken);
            }
            catch (Exception gone) when (gone is IOException or OperationCanceledException)
            {
                Log.HookLeft(logger, gone);
            }
        }
    }

    /// <summary>The event to its end, within <see cref="Reading"/>; a client still sending past it is told it took too long.</summary>
    private async Task<string> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Reading, clock);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var read = new MemoryStream();
        var buffer = new byte[4096];

        try
        {
            while (read.Length < Largest && await stream.ReadAsync(buffer, reading.Token) is var count and > 0)
                await read.WriteAsync(buffer.AsMemory(0, count), reading.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"no whole event came within {Reading.TotalSeconds:0} s");
        }

        return Encoding.UTF8.GetString(read.GetBuffer(), 0, (int)read.Length);
    }

    /// <summary>A stop asks for a census without waiting for it, and a stop that comes while one is under way asks for nothing more.</summary>
    private void CountInTheBackground()
    {
        if (Interlocked.Exchange(ref _isCounting, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await hooks.CensusAsync(CancellationToken.None);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                Log.StopCensusFailed(logger, failure);
            }
            finally
            {
                Volatile.Write(ref _isCounting, 0);
            }
        });
    }

    private void Forget()
    {
        try
        {
            File.Delete(config.HookSocket);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            Log.HookSocketNotRemoved(logger, refused);
        }
    }
}
