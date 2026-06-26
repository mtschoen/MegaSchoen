using Claude.Core;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

[TestClass]
public class TranscriptTitleReaderTests
{
    [TestMethod]
    public void ExtractTitle_NoTitleLines_ReturnsNull()
    {
        var lines = new[]
        {
            """{"type":"user","message":{}}""",
            """{"type":"assistant","message":{}}""",
        };

        Assert.IsNull(TranscriptTitleReader.ExtractTitle(lines));
    }

    [TestMethod]
    public void ExtractTitle_SingleTitle_ReturnsIt()
    {
        var lines = new[]
        {
            """{"type":"user","message":{}}""",
            """{"type":"ai-title","aiTitle":"Fix the login bug","sessionId":"s1"}""",
        };

        Assert.AreEqual("Fix the login bug", TranscriptTitleReader.ExtractTitle(lines));
    }

    [TestMethod]
    public void ExtractTitle_MultipleTitles_LastWins()
    {
        var lines = new[]
        {
            """{"type":"ai-title","aiTitle":"First guess","sessionId":"s1"}""",
            """{"type":"assistant","message":{}}""",
            """{"type":"ai-title","aiTitle":"Refined title","sessionId":"s1"}""",
        };

        Assert.AreEqual("Refined title", TranscriptTitleReader.ExtractTitle(lines));
    }

    [TestMethod]
    public void ExtractTitle_IgnoresMalformedAndBlankLines()
    {
        var lines = new[]
        {
            "",
            "not json at all",
            """{"type":"ai-title","aiTitle":"Good title","sessionId":"s1"}""",
            "{ broken json",
        };

        Assert.AreEqual("Good title", TranscriptTitleReader.ExtractTitle(lines));
    }

    [TestMethod]
    public void ExtractTitle_EmptyTitleValue_DoesNotOverridePrevious()
    {
        var lines = new[]
        {
            """{"type":"ai-title","aiTitle":"Real title","sessionId":"s1"}""",
            """{"type":"ai-title","aiTitle":"","sessionId":"s1"}""",
        };

        Assert.AreEqual("Real title", TranscriptTitleReader.ExtractTitle(lines));
    }

    [TestMethod]
    public void ReadTitle_MissingFile_ReturnsNull()
    {
        Assert.IsNull(TranscriptTitleReader.ReadTitle(@"C:\does\not\exist.jsonl"));
    }

    [TestMethod]
    public void ReadTitle_EmptyPath_ReturnsNull()
    {
        Assert.IsNull(TranscriptTitleReader.ReadTitle(""));
    }

    [TestMethod]
    public void ReadTitle_ReadsTitleFromTranscriptFile()
    {
        using var fixture = new ClaudeProjectsFixture();
        var lines = new[]
        {
            """{"type":"user","message":{}}""",
            """{"type":"ai-title","aiTitle":"Add session titles","sessionId":"s1"}""",
            """{"type":"assistant","message":{}}""",
        };
        var path = fixture.AddSession("slug", "s1", lines, DateTime.UtcNow);

        Assert.AreEqual("Add session titles", TranscriptTitleReader.ReadTitle(path));
    }

    [TestMethod]
    public void ReadTitle_TitleNearEndOfLargeTranscript_FoundViaTailRead()
    {
        using var fixture = new ClaudeProjectsFixture();
        // A title set early, then >256KB of later traffic, then the current
        // title re-emitted near EOF (Claude Code re-writes it as the session
        // evolves). The tail read must surface the latest one.
        var lines = new List<string> { """{"type":"ai-title","aiTitle":"Stale early title","sessionId":"s1"}""" };
        var filler = "{\"type\":\"assistant\",\"message\":{\"content\":\"" + new string('x', 500) + "\"}}";
        for (var i = 0; i < 800; i++)
        {
            lines.Add(filler);
        }
        lines.Add("""{"type":"ai-title","aiTitle":"Current title","sessionId":"s1"}""");
        lines.Add("""{"type":"assistant","message":{}}""");
        var path = fixture.AddSession("slug", "s1", lines, DateTime.UtcNow);

        Assert.AreEqual("Current title", TranscriptTitleReader.ReadTitle(path));
    }
}
