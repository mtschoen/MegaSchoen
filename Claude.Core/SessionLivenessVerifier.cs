using System.Text.Json;
using Claude.Core.Models;

namespace Claude.Core;

public enum LastEntryClass
{
    SessionPending,
    Resolved
}

public sealed class SessionLivenessVerifier
{
    static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);
    const int TailReadChunkSize = 4096;
    const int TailReadMaxBytes = 256 * 1024;

    readonly TimeSpan _grace;

    public SessionLivenessVerifier(TimeSpan? grace = null)
    {
        _grace = grace ?? DefaultGrace;
    }

    public bool IsStillWaiting(SessionEntry entry)
    {
        if (string.IsNullOrEmpty(entry.TranscriptPath) || !File.Exists(entry.TranscriptPath))
        {
            return false;
        }

        var transcriptTouchedAt = File.GetLastWriteTimeUtc(entry.TranscriptPath);
        var threshold = entry.NotifiedAt.UtcDateTime + _grace;

        if (transcriptTouchedAt <= threshold)
        {
            return true;
        }

        return ClassifyLastEntry(entry.TranscriptPath) == LastEntryClass.SessionPending;
    }

    public static LastEntryClass ClassifyLastEntry(string transcriptPath)
    {
        string? lastLine;
        try
        {
            lastLine = ReadLastNonEmptyLine(transcriptPath);
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionLivenessVerifier: tail read failed for {transcriptPath}: {exception.Message}");
            return LastEntryClass.SessionPending;
        }

        if (string.IsNullOrWhiteSpace(lastLine))
        {
            return LastEntryClass.Resolved;
        }

        string? type;
        try
        {
            using var doc = JsonDocument.Parse(lastLine);
            type = doc.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return LastEntryClass.SessionPending;
        }

        return type switch
        {
            "assistant" => LastEntryClass.SessionPending,
            "user"        => LastEntryClass.Resolved,
            "tool_result" => LastEntryClass.Resolved,
            "system"      => LastEntryClass.Resolved,
            _             => LastEntryClass.Resolved
        };
    }

    static string? ReadLastNonEmptyLine(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length == 0)
        {
            return null;
        }

        var totalRead = 0;
        var buffer = new List<byte>(capacity: TailReadChunkSize);

        while (totalRead < stream.Length && totalRead < TailReadMaxBytes)
        {
            var chunkSize = (int)Math.Min(TailReadChunkSize, stream.Length - totalRead);
            stream.Seek(-(totalRead + chunkSize), SeekOrigin.End);

            var chunk = new byte[chunkSize];
            var bytesRead = stream.Read(chunk, 0, chunkSize);
            buffer.InsertRange(0, chunk.AsSpan(0, bytesRead).ToArray());
            totalRead += bytesRead;

            var trimmedEnd = buffer.Count;
            while (trimmedEnd > 0 && (buffer[trimmedEnd - 1] == (byte)'\n' || buffer[trimmedEnd - 1] == (byte)'\r'))
            {
                trimmedEnd--;
            }

            if (trimmedEnd == 0)
            {
                if (totalRead >= stream.Length) return null;
                continue;
            }

            var newlineIndex = -1;
            for (var i = trimmedEnd - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    newlineIndex = i;
                    break;
                }
            }

            if (newlineIndex >= 0)
            {
                var lineBytes = buffer.GetRange(newlineIndex + 1, trimmedEnd - newlineIndex - 1);
                return System.Text.Encoding.UTF8.GetString(lineBytes.ToArray());
            }

            if (totalRead >= stream.Length)
            {
                return System.Text.Encoding.UTF8.GetString(buffer.GetRange(0, trimmedEnd).ToArray());
            }
        }

        throw new IOException($"Transcript tail exceeded {TailReadMaxBytes} bytes without finding a line boundary");
    }
}
