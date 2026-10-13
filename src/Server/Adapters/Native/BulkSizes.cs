using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Snail.MCP.CleanUp.Adapters.Native;

/// <summary>The space a directory tree takes on disk, read a directory at a time with <c>getattrlistbulk</c>.</summary>
/// <remarks>
/// Counted file by file through .NET's enumeration, the size of a tree costs a <c>stat</c> per file: a scratchpad of
/// 290 730 files took 1.9 s and 3.9 MB, measured on 2026-10-13. The kernel hands out the names, types and sizes of a whole
/// directory in one call instead — 0.41 s and 1.3 MB for the same tree — and the size it hands out is the space the
/// blocks take, the one <c>du</c> counts and a removal gives back, not the length a file claims.
/// <para>
/// Subdirectories are opened by name relative to the one being read, never through a link: a link to a volume of millions
/// of files is one entry, as it is in the file trail.
/// </para>
/// <para>
/// The buffer is rented, not made per count: the folders of 2 000 ended test runs counted one by one took 64 KB each,
/// 128 MB of a census (CensusBenchmarks, 2026-10-13).
/// </para>
/// </remarks>
internal static unsafe partial class BulkSizes
{
    /// <summary>How deep the tree is read before the count stops and says so; beyond it lies a loop or a build gone wrong.</summary>
    public const int DeepestLevel = 256;

    private const int ReadOnlyDirectory = 0x0010_0000 | 0x0100_0000;

    private const int NotFollowed = 0x0000_0100;

    private const uint ReturnedAttributes = 0x8000_0000;

    private const uint ErrorAttribute = 0x2000_0000;

    private const uint NameAttribute = 0x0000_0001;

    private const uint TypeAttribute = 0x0000_0008;

    private const uint AllocatedSizeAttribute = 0x0000_0004;

    private const uint DirectoryType = 2;

    private const int BufferSize = 64 * 1024;

    /// <summary>The bytes the files under a directory take and whether all of them were counted; null when the directory cannot be opened.</summary>
    /// <param name="directory">The directory whose tree is counted.</param>
    /// <param name="mostEntries">How many entries are counted before the count stops.</param>
    public static Count? Of(string directory, long mostEntries)
    {
        var descriptor = Open(directory, ReadOnlyDirectory);

        if (descriptor < 0)
            return null;

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            var counting = new Counting(buffer, mostEntries);

            counting.Read(descriptor, 0);

            return new Count(counting.Bytes, counting.IsComplete);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>What a count found: the bytes, and whether it read every entry or stopped short.</summary>
    public readonly record struct Count(long Bytes, bool IsComplete);

    /// <summary>One count under way: the buffer every directory is read into, and the totals so far.</summary>
    private sealed class Counting(byte[] buffer, long mostEntries)
    {
        public long Bytes { get; private set; }

        public bool IsComplete { get; private set; } = true;

        private long _entries;

        /// <summary>Reads the directory open as <paramref name="descriptor"/>, then the ones below it, and closes it.</summary>
        public void Read(int descriptor, int level)
        {
            try
            {
                foreach (var name in Listed(descriptor))
                {
                    if (!IsComplete)
                        return;

                    if (level + 1 >= DeepestLevel)
                    {
                        IsComplete = false;

                        return;
                    }

                    var below = OpenBelow(descriptor, name, ReadOnlyDirectory | NotFollowed);

                    if (below < 0)
                    {
                        IsComplete = false;

                        continue;
                    }

                    Read(below, level + 1);
                }
            }
            finally
            {
                _ = Close(descriptor);
            }
        }

        /// <summary>Adds up the files of one directory and returns the names of its subdirectories, to be read after it is done.</summary>
        private List<string> Listed(int descriptor)
        {
            var directories = new List<string>();
            var asked = new AttributeList
            {
                BitmapCount = 5,
                Common = ReturnedAttributes | ErrorAttribute | NameAttribute | TypeAttribute,
                File = AllocatedSizeAttribute
            };

            fixed (byte* start = buffer)
            {
                while (IsComplete)
                {
                    var count = BulkAttributes(descriptor, &asked, start, (nuint)buffer.Length, 0);

                    if (count < 0)
                        IsComplete = false;

                    if (count <= 0)
                        break;

                    var at = 0;

                    for (var index = 0; index < count && IsComplete; index++)
                    {
                        var entry = buffer.AsSpan(at);
                        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry);

                        Added(entry[..length], directories);
                        at += length;
                    }
                }
            }

            return directories;
        }

        /// <summary>
        /// One entry as the kernel lays it out: its length, the attributes it returned, then each of them in order — the error,
        /// the name as an offset from where the reference stands, the type, and for a file the space its blocks take.
        /// </summary>
        private void Added(ReadOnlySpan<byte> entry, List<string> directories)
        {
            if (++_entries > mostEntries)
            {
                IsComplete = false;

                return;
            }

            var common = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var file = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            var at = 24;

            if ((common & ErrorAttribute) != 0)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(entry[at..]) != 0)
                    IsComplete = false;

                at += 4;
            }

            var reference = at;

            if ((common & NameAttribute) != 0)
                at += 8;

            var type = 0u;

            if ((common & TypeAttribute) != 0)
            {
                type = BinaryPrimitives.ReadUInt32LittleEndian(entry[at..]);
                at += 4;
            }

            if (type == DirectoryType && (common & NameAttribute) != 0)
            {
                var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[reference..]);
                var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[(reference + 4)..]);
                var name = entry.Slice(reference + offset, Math.Max(size - 1, 0));

                directories.Add(System.Text.Encoding.UTF8.GetString(name));

                return;
            }

            if ((file & AllocatedSizeAttribute) != 0)
                Bytes += BinaryPrimitives.ReadInt64LittleEndian(entry[at..]);
        }
    }

    /// <summary>The attributes asked of every entry: <c>struct attrlist</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        public ushort BitmapCount;

        public ushort Reserved;

        public uint Common;

        public uint Volume;

        public uint Directory;

        public uint File;

        public uint Fork;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenBelow(int directory, string name, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "getattrlistbulk")]
    private static partial int BulkAttributes(int directory, AttributeList* asked, byte* buffer, nuint size, ulong options);
}
