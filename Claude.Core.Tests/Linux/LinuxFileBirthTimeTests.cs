using Claude.Core.Linux;

namespace Claude.Core.Tests.Linux;

// The runtime contract behind issue #37: session pairing keys on transcript
// creation time, and on Linux .NET's File.GetCreationTimeUtc tracks mtime, so
// the enumerator must read the statx birth time instead. These tests exercise
// the real filesystem (tmp), not a fake. OS-conditional because the net10.0
// TFM also runs on the Windows CI runner, where statx does not exist.
[TestClass]
[OSCondition(OperatingSystems.Linux)]
public class LinuxFileBirthTimeTests
{
    string _path = "";

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"birth-{Guid.NewGuid():N}.jsonl");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [TestMethod]
    public void TryGetBirthTimeUtc_FreshFile_ReportsNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        File.WriteAllText(_path, "{}\n");
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.IsTrue(LinuxFileBirthTime.TryGetBirthTimeUtc(_path, out var birth),
            "tmp filesystems on any modern Linux report a statx birth time");
        Assert.IsTrue(birth >= before && birth <= after,
            $"birth time {birth:O} should be within the write window {before:O}..{after:O}");
    }

    [TestMethod]
    public void TryGetBirthTimeUtc_SurvivesMtimeChanges()
    {
        // The failure mode this exists to prevent: an actively written (or
        // backdated) transcript must not have its creation time dragged along
        // with mtime, or causal session pairing degrades at runtime.
        File.WriteAllText(_path, "{}\n");
        var mtime = DateTime.UtcNow.AddMinutes(-30);
        File.SetLastWriteTimeUtc(_path, mtime);

        Assert.IsTrue(LinuxFileBirthTime.TryGetBirthTimeUtc(_path, out var birth));
        Assert.IsTrue(birth > mtime.AddMinutes(1),
            "birth time reflects actual file creation, not the backdated mtime");
        Assert.AreEqual(birth, TranscriptCreationTime.GetUtc(_path),
            "TranscriptCreationTime routes through the birth time on Linux");
    }

    [TestMethod]
    public void TryGetBirthTimeUtc_MissingFile_ReturnsFalse()
    {
        Assert.IsFalse(LinuxFileBirthTime.TryGetBirthTimeUtc(_path, out _));
    }
}
