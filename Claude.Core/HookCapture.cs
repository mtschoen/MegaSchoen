namespace Claude.Core;

// Diagnostic tee for capturing ground-truth hook payloads. When the
// MEGASCHOEN_HOOK_CAPTURE environment variable is set, every raw hook stdin is
// appended verbatim (before deserialization) as one NDJSON line, so we can see
// exactly what Claude Code sends per event — including fields HookPayload does
// not model. Off by default; never throws (runs inside hook handlers).
//
// Enable by launching claude with the variable set so the spawned hook process
// inherits it:
//   $env:MEGASCHOEN_HOOK_CAPTURE = "1"; claude        (default path)
//   $env:MEGASCHOEN_HOOK_CAPTURE = "C:\tmp\hooks.ndjson"; claude
public static class HookCapture
{
    const string EnableVariable = "MEGASCHOEN_HOOK_CAPTURE";

    static readonly object Lock = new();

    public static bool IsEnabled => ResolveTarget() is not null;

    public static void Capture(string rawStdin)
    {
        var target = ResolveTarget();
        if (target is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = BuildLine(rawStdin) + Environment.NewLine;
            lock (Lock)
            {
                File.AppendAllText(target, line);
            }
        }
        catch
        {
            // Never throw from the capture tee.
        }
    }

    // Embeds the raw payload as a nested JSON value when it parses, so the
    // capture file stays queryable NDJSON; otherwise keeps it as escaped text.
    static string BuildLine(string rawStdin)
    {
        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("capturedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(rawStdin);
                writer.WritePropertyName("payload");
                document.RootElement.WriteTo(writer);
            }
            catch (System.Text.Json.JsonException)
            {
                writer.WriteString("rawText", rawStdin);
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    static string? ResolveTarget()
    {
        var value = Environment.GetEnvironmentVariable(EnableVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() is "1" or "true" or "on"
            ? Paths.HookCaptureLog
            : value;
    }
}
