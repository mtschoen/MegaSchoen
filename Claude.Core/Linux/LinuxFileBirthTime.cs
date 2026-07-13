using System.Runtime.InteropServices;

namespace Claude.Core.Linux;

// .NET's File.GetCreationTimeUtc on Linux does not surface the filesystem
// birth time (btime): it reports a value that tracks mtime, so an actively
// written transcript's "creation" time drifts forward with every write and
// the enumerator's start-time / causal pairing degrades (issue #37). The
// kernel does track btime on the filesystems we care about (ext4, tmpfs);
// statx(2) exposes it. This helper reads it directly, reporting false when
// the filesystem or kernel does not provide one so callers can fall back.
static partial class LinuxFileBirthTime
{
    const int AT_FDCWD = -100;
    const uint STATX_BTIME = 0x00000800;

    // Kernel struct statx is 256 bytes; only the fields we read are mapped.
    // Offsets per include/uapi/linux/stat.h: stx_mask at 0, stx_btime
    // (statx_timestamp: __s64 tv_sec, __u32 tv_nsec) at 80.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    struct StatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(80)] public long BirthTimeSeconds;
        [FieldOffset(88)] public uint BirthTimeNanoseconds;
    }

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int dirfd, string pathname, int flags, uint mask, ref StatxBuffer buffer);

    public static bool TryGetBirthTimeUtc(string path, out DateTime birthTimeUtc)
    {
        birthTimeUtc = default;
        var buffer = default(StatxBuffer);
        try
        {
            if (Statx(AT_FDCWD, path, flags: 0, STATX_BTIME, ref buffer) != 0)
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Pre-glibc-2.28 libc without a statx wrapper: nothing to read.
            return false;
        }

        if ((buffer.Mask & STATX_BTIME) == 0)
        {
            // Kernel or filesystem does not report a birth time for this path.
            return false;
        }

        birthTimeUtc = DateTime.UnixEpoch
            .AddSeconds(buffer.BirthTimeSeconds)
            .AddTicks(buffer.BirthTimeNanoseconds / 100);
        return true;
    }
}
