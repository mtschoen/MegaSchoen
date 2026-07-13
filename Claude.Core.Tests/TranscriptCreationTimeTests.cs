namespace Claude.Core.Tests;

[TestClass]
public class TranscriptCreationTimeTests
{
    [TestMethod]
    public void GetUtc_FreshFile_ReportsNowOnEveryPlatform()
    {
        // The enumerator's default creation-time source must report the real
        // creation moment on both platforms: File.GetCreationTimeUtc on
        // Windows, the statx birth time on Linux (where the .NET API would
        // drift with mtime; issue #37).
        var path = Path.Combine(Path.GetTempPath(), $"transcript-{Guid.NewGuid():N}.jsonl");
        try
        {
            var before = DateTime.UtcNow.AddSeconds(-5);
            File.WriteAllText(path, "{}\n");
            var after = DateTime.UtcNow.AddSeconds(5);

            var creation = TranscriptCreationTime.GetUtc(path);
            Assert.IsTrue(creation >= before && creation <= after,
                $"creation {creation:O} should be within the write window {before:O}..{after:O}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
