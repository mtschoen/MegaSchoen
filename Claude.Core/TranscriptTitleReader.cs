using System.Text;
using System.Text.Json;

namespace Claude.Core;

// Reads a session's human-facing title — Claude Code's generated "ai-title" —
// from its transcript. The title is re-emitted near the end of the transcript
// as the conversation evolves, so a bounded tail read reliably catches the
// current one (rig-verified: across every titled transcript on disk the last
// ai-title sat within TailReadMaxBytes of EOF) without paying a full read of a
// multi-MB transcript on every dashboard refresh.
public static class TranscriptTitleReader
{
    const int TailReadMaxBytes = 256 * 1024;

    public static string? ReadTitle(string transcriptPath)
    {
        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath))
        {
            return null;
        }

        try
        {
            return ExtractTitle(ReadTailLines(transcriptPath));
        }
        catch (Exception exception)
        {
            Logger.Log($"TranscriptTitleReader: read failed for {transcriptPath}: {exception.Message}");
            return null;
        }
    }

    // Pure: the aiTitle of the last well-formed "ai-title" entry among the given
    // JSONL lines, or null when none carries a non-empty one. Blank, non-JSON,
    // and non-title lines are ignored; an empty aiTitle does not overwrite a
    // prior real title.
    public static string? ExtractTitle(IEnumerable<string> jsonlLines)
    {
        string? title = null;
        foreach (var line in jsonlLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.Contains("\"ai-title\"", StringComparison.Ordinal)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() == "ai-title"
                    && root.TryGetProperty("aiTitle", out var aiTitle)
                    && aiTitle.GetString() is { Length: > 0 } value)
                {
                    title = value;
                }
            }
            catch (JsonException)
            {
                // Malformed line — ignore, keep scanning.
            }
        }
        return title;
    }

    // The lines wholly contained in the last TailReadMaxBytes of the file. When
    // the read starts mid-file the first (partial) line is discarded so callers
    // never parse a truncated JSON object.
    static List<string> ReadTailLines(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var start = Math.Max(0, stream.Length - TailReadMaxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        if (start > 0)
        {
            reader.ReadLine();
        }

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }
        return lines;
    }
}
