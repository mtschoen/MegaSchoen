namespace Claude.Core;

// The one place that answers "when was this transcript created?" for session
// pairing. On Windows, File.GetCreationTimeUtc is the real creation time. On
// Linux, .NET's File.GetCreationTimeUtc tracks mtime (it never surfaces the
// filesystem birth time), which would make an actively written transcript
// look freshly created and break the enumerator's start-time / causal
// pairing (issue #37) - so the Linux build reads the statx(2) birth time and
// only falls back to File.GetCreationTimeUtc (that is, mtime) on filesystems
// that do not record one.
public static class TranscriptCreationTime
{
    public static DateTime GetUtc(string path)
    {
#if WINDOWS
        return File.GetCreationTimeUtc(path);
#else
        if (OperatingSystem.IsLinux() && Linux.LinuxFileBirthTime.TryGetBirthTimeUtc(path, out var birthTimeUtc))
        {
            return birthTimeUtc;
        }
        return File.GetCreationTimeUtc(path);
#endif
    }
}
