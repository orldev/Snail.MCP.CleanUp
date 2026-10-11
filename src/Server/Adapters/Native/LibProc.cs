using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Snail.MCP.CleanUp.Adapters.Native;

/// <summary>The process table as the macOS kernel reports it, read in this process instead of by running ps.</summary>
/// <remarks>
/// Two runs of ps cost a census a fork, an exec and a parse of text printed in the machine's locale, every half a
/// minute in every session. libproc answers the same questions from inside the process — and better: the start of
/// a process to the microsecond rather than to the second, and the path of its program whole, spaces included,
/// where ps cuts the command line at the first one.
/// <para>
/// The full record of a process, its start included, is given only for the processes of the same user. The
/// system's own get the short record — parent and name — and the start of the epoch, which keeps their keys
/// stable from one census to the next; nothing of another user is ever this server's to release.
/// </para>
/// </remarks>
internal static partial class LibProc
{
    private const int BsdInfoFlavor = 3;

    private const int BsdInfoSize = 136;

    private const int ShortInfoFlavor = 13;

    private const int ShortInfoSize = 64;

    private const int PathMaxSize = 4096;

    private const int KernelControl = 1;

    private const int ProcessArgumentsTwo = 49;

    /// <summary>Every process the kernel lists; empty when it cannot be asked.</summary>
    public static IReadOnlyList<RunningProcess> List()
    {
        var count = ListAllPids(null, 0);

        if (count <= 0)
            return [];

        var pids = new int[count + 64];
        var listed = ListAllPids(pids, pids.Length * sizeof(int));
        var info = new byte[Math.Max(BsdInfoSize, ShortInfoSize)];
        var path = new byte[PathMaxSize];
        var arguments = new byte[256 * 1024];
        var processes = new List<RunningProcess>(listed);

        for (var index = 0; index < listed; index++)
        {
            if (Described(pids[index], info, path, arguments) is { } process)
                processes.Add(process);
        }

        return processes;
    }

    private static RunningProcess? Described(int pid, byte[] info, byte[] path, byte[] arguments)
    {
        if (pid <= 0)
            return null;

        if (PidInfo(pid, BsdInfoFlavor, 0, info, BsdInfoSize) != BsdInfoSize)
            return Others(pid, info, path);

        var parent = (int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(16));
        var seconds = (long)BinaryPrimitives.ReadUInt64LittleEndian(info.AsSpan(120));
        var microseconds = (long)BinaryPrimitives.ReadUInt64LittleEndian(info.AsSpan(128));
        var executable = PidPath(pid, path, PathMaxSize) > 0 ? Text(path) : null;
        var words = Arguments(pid, arguments);
        var command = words is { Count: > 0 } ? string.Join(' ', words) : executable ?? Text(info.AsSpan(48, 16));
        var started = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(microseconds * 10);
        var name = words is { Count: > 0 } ? Path.GetFileName(words[0]) : null;

        return new RunningProcess(pid, parent, started.ToLocalTime(), command) { Executable = executable, Name = name };
    }

    /// <summary>A process of another user, from the short record: its parent and its name, its program's path when the kernel tells it.</summary>
    private static RunningProcess? Others(int pid, byte[] info, byte[] path)
    {
        if (PidInfo(pid, ShortInfoFlavor, 0, info, ShortInfoSize) != ShortInfoSize)
            return null;

        var parent = (int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(4));
        var executable = PidPath(pid, path, PathMaxSize) > 0 ? Text(path) : null;

        return new RunningProcess(pid, parent, DateTimeOffset.UnixEpoch, executable ?? Text(info.AsSpan(16, 16))) { Executable = executable };
    }

    /// <summary>
    /// The arguments of a process from <c>KERN_PROCARGS2</c>: a count, the path it was started from, padding, then the
    /// arguments one after another, each ended by a zero. Only the processes of the same user answer; for the rest the
    /// path of the program stands in, which is all a census needs of the system's own processes.
    /// </summary>
    private static List<string>? Arguments(int pid, byte[] buffer)
    {
        Span<int> name = [KernelControl, ProcessArgumentsTwo, pid];
        nint size = buffer.Length;

        if (Sysctl(name, (uint)name.Length, buffer, ref size, nint.Zero, 0) != 0 || size < sizeof(int))
            return null;

        var bytes = buffer.AsSpan(0, (int)size);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var at = sizeof(int);

        at += Math.Max(bytes[at..].IndexOf((byte)0), 0);

        while (at < bytes.Length && bytes[at] == 0)
            at++;

        var words = new List<string>(count);

        while (words.Count < count && at < bytes.Length)
        {
            var end = bytes[at..].IndexOf((byte)0);
            var length = end < 0 ? bytes.Length - at : end;

            words.Add(Encoding.UTF8.GetString(bytes.Slice(at, length)));
            at += length + 1;
        }

        return words.Count > 0 ? words : null;
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);

        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }

    [LibraryImport("libproc", EntryPoint = "proc_listallpids")]
    private static partial int ListAllPids([Out] int[]? buffer, int size);

    [LibraryImport("libproc", EntryPoint = "proc_pidinfo")]
    private static partial int PidInfo(int pid, int flavor, ulong argument, [Out] byte[] buffer, int size);

    [LibraryImport("libproc", EntryPoint = "proc_pidpath")]
    private static partial int PidPath(int pid, [Out] byte[] buffer, uint size);

    [LibraryImport("libc", EntryPoint = "sysctl")]
    private static partial int Sysctl(ReadOnlySpan<int> name, uint length, [Out] byte[] old, ref nint oldLength, nint replacement, nint replacementLength);
}
