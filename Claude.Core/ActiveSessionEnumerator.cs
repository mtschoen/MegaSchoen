using Claude.Core.Models;

namespace Claude.Core;

public sealed class ActiveSessionEnumerator
{
    // When attaching a terminal window to a session for the Focus button, a
    // claude process's StartTime should land within a second or two of its
    // transcript's creation time. 30s absorbs clock skew / slow startup. This
    // is used ONLY for best-effort window attach now — never for identity.
    static readonly TimeSpan StartTimeMatchTolerance = TimeSpan.FromSeconds(30);

    readonly IClaudeProcessLocator _locator;
    readonly StateStore _store;
    readonly string _projectsRoot;

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store, string projectsRoot)
    {
        _locator = locator;
        _store = store;
        _projectsRoot = projectsRoot;
    }

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store)
        : this(locator, store, DefaultProjectsRoot()) { }

    static string DefaultProjectsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public IReadOnlyList<SessionSnapshot> Enumerate()
    {
        var liveProcesses = _locator.EnumerateLiveSessions();
        var stateBySessionId = _store.Read();

        // Processes that carry an authoritative session id (background/daemon
        // workers) are surfaced by id in the pass below; only anonymous
        // (foreground/headless) processes feed the cwd-keyed correlation + cap.
        // cwd is the liveness unit for the anonymous set: a session is live iff a
        // claude process runs in its cwd.
        var procsByCwd = liveProcesses
            .Where(p => p.SessionId is null && !string.IsNullOrEmpty(p.WorkingDirectory))
            .GroupBy(p => NormalizeCwd(p.WorkingDirectory), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var snapshots = new List<SessionSnapshot>();
        foreach (var (cwd, procs) in procsByCwd)
        {
            snapshots.AddRange(BuildCwdSessions(cwd, procs, stateBySessionId));
        }

        AddAuthoritativeIdSessions(liveProcesses, stateBySessionId, snapshots);

        snapshots.Sort(CompareForDisplay);
        return snapshots;
    }

    // The cwd-keyed pass for one anonymous-process cwd: builds candidate
    // sessions (transcripts on disk ∪ StateStore entries, keyed by real session
    // id), caps them to the live-process count, and emits a snapshot each.
    List<SessionSnapshot> BuildCwdSessions(
        string cwd, List<ClaudeWindow> procs, IReadOnlyDictionary<string, SessionEntry> stateBySessionId)
    {
        var slugDir = Path.Combine(_projectsRoot, SlugEncoder.Encode(cwd));

        // Candidate sessions for THIS cwd, keyed by real session id. Source
        // of truth = transcripts on disk ∪ StateStore entries. Identity is
        // the file/key name — never a window guess.
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(slugDir))
        {
            foreach (var path in Directory.GetFiles(slugDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                // Slug-collision guard: only accept a transcript whose own
                // recorded cwd matches this cwd (null = unreadable = accept).
                if (!CwdMatches(ReadTranscriptCwd(path), cwd)) continue;
                var id = Path.GetFileNameWithoutExtension(path);
                candidates[id] = new Candidate(
                    id, path, File.GetCreationTimeUtc(path), File.GetLastWriteTimeUtc(path));
            }
        }

        foreach (var (id, entry) in stateBySessionId)
        {
            if (!CwdMatches(entry.Cwd, cwd)) continue;
            if (candidates.ContainsKey(id)) continue; // transcript already covered it
            var path = entry.TranscriptPath ?? "";
            var hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);
            // Transcript-less store entries fall back to NotifiedAt for both
            // timestamps, so they rank in the freshest-N cap on a different
            // clock than transcript-backed peers (file mtime). Edge-only: a
            // live store entry almost always carries a transcript path.
            var creation = hasFile ? File.GetCreationTimeUtc(path) : entry.NotifiedAt.UtcDateTime;
            var lastWrite = hasFile ? File.GetLastWriteTimeUtc(path) : entry.NotifiedAt.UtcDateTime;
            candidates[id] = new Candidate(id, path, creation, lastWrite);
        }

        var result = new List<SessionSnapshot>();
        if (candidates.Count == 0) return result;

        // Shared-cwd cap: at most procs.Count sessions are alive here. Keep
        // the freshest by last write (relative rank — NOT a time cutoff).
        var kept = candidates.Values
            .OrderByDescending(c => c.LastWriteUtc)
            .Take(procs.Count)
            .ToList();

        var usedProcesses = new HashSet<uint>();
        foreach (var candidate in kept)
        {
            var entry = stateBySessionId.TryGetValue(candidate.Id, out var e) ? e : null;
            var state = SessionStateClassifier.Classify(entry, candidate.TranscriptPath);
            var (window, title, sshClientPort) = AttachWindow(procs, candidate.CreationUtc, usedProcesses);
            // Neither the window nor the SSH client port may depend solely on
            // AttachWindow's start-time match: when the cwd has a single live
            // process the session-to-process association is unambiguous, so its
            // facts attach directly. The match misses in two rig-verified ways:
            //  - Linux remote: .NET's file creation time tracks mtime, so an
            //    active session's transcript drifts past the 30s tolerance and
            //    the piggybacked port is dropped (Linux has no window anyway).
            //  - Windows (Rider find, 2026-06-10): a freshly started claude has
            //    no transcript until the first prompt, so the cwd's freshest OLD
            //    transcript surfaces as the session; its creation time is days
            //    from the process start and Focus grays out without this attach.
            // Multi-session-per-cwd stays best-effort (no guessing).
            if (window.IsZero && procs.Count == 1 && !usedProcesses.Contains(procs[0].ProcessId))
            {
                usedProcesses.Add(procs[0].ProcessId);
                window = procs[0].Window;
                title = procs[0].Title;
            }
            sshClientPort ??= procs.Count == 1 ? procs[0].SshClientPort : null;
            var subagents = EnumerateSubagents(slugDir, candidate.Id);

            result.Add(new SessionSnapshot(
                SessionId: candidate.Id,
                Cwd: cwd,
                TranscriptPath: candidate.TranscriptPath,
                LastActivityUtc: new DateTimeOffset(candidate.LastWriteUtc, TimeSpan.Zero),
                State: state,
                PendingMessage: entry?.Message,
                Window: window,
                WindowTitle: string.IsNullOrEmpty(title) ? null : title,
                Subagents: subagents,
                SshClientPort: sshClientPort));
        }
        return result;
    }

    // Authoritative-id pass (design rule: authoritative identity wins). A
    // background/daemon worker carries its real session_id on the command
    // line — surface it by id even when its cwd has no transcript, is the
    // shared home dir, or the anonymous cwd cap would have dropped it. The
    // hook-recorded cwd (store entry) is preferred over the worker's PEB cwd,
    // which is often just the daemon's home dir.
    void AddAuthoritativeIdSessions(
        IReadOnlyList<ClaudeWindow> liveProcesses,
        IReadOnlyDictionary<string, SessionEntry> stateBySessionId,
        List<SessionSnapshot> snapshots)
    {
        var emitted = new HashSet<string>(snapshots.Select(s => s.SessionId), StringComparer.OrdinalIgnoreCase);
        foreach (var process in liveProcesses)
        {
            if (process.SessionId is not { } id || !emitted.Add(id)) continue;

            var entry = stateBySessionId.TryGetValue(id, out var e) ? e : null;
            var cwd = NormalizeCwd(entry?.Cwd ?? process.WorkingDirectory ?? "");
            var slugDir = cwd.Length > 0 ? Path.Combine(_projectsRoot, SlugEncoder.Encode(cwd)) : "";

            var transcriptPath = entry?.TranscriptPath;
            if (string.IsNullOrEmpty(transcriptPath) && slugDir.Length > 0)
            {
                var byId = Path.Combine(slugDir, $"{id}.jsonl");
                if (File.Exists(byId)) transcriptPath = byId;
            }
            var existingTranscript = !string.IsNullOrEmpty(transcriptPath) && File.Exists(transcriptPath)
                ? transcriptPath
                : null;
            var lastWrite = existingTranscript is not null
                ? File.GetLastWriteTimeUtc(existingTranscript)
                : entry?.NotifiedAt.UtcDateTime ?? DateTime.UtcNow;

            snapshots.Add(new SessionSnapshot(
                SessionId: id,
                Cwd: cwd,
                TranscriptPath: existingTranscript ?? "",
                LastActivityUtc: new DateTimeOffset(lastWrite, TimeSpan.Zero),
                State: SessionStateClassifier.Classify(entry, existingTranscript ?? ""),
                PendingMessage: entry?.Message,
                Window: process.Window,
                WindowTitle: null,
                Subagents: existingTranscript is not null && slugDir.Length > 0
                    ? EnumerateSubagents(slugDir, id)
                    : Array.Empty<SubagentSnapshot>(),
                SshClientPort: process.SshClientPort));
        }
    }

    sealed record Candidate(string Id, string TranscriptPath, DateTime CreationUtc, DateTime LastWriteUtc);

    // Best-effort: pick an unused live process whose StartTime is within
    // tolerance of the transcript's creation time, and surface its window.
    // Returns (Null, "") when no confident match or the matched process is
    // windowless.
    static (WindowToken Window, string Title, int? SshClientPort) AttachWindow(
        List<ClaudeWindow> procs, DateTime creationUtc, HashSet<uint> usedProcesses)
    {
        ClaudeWindow? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var p in procs)
        {
            if (usedProcesses.Contains(p.ProcessId)) continue;
            var delta = (p.StartTimeUtc.UtcDateTime - creationUtc).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = p;
            }
        }
        if (best is { } match && bestDelta <= StartTimeMatchTolerance)
        {
            usedProcesses.Add(match.ProcessId);
            return (match.Window, match.Title, match.SshClientPort);
        }
        return (WindowToken.Null, string.Empty, null);
    }

    static string NormalizeCwd(string? cwd) => (cwd ?? "").TrimEnd('\\', '/');

    static bool CwdMatches(string? a, string b) =>
        a is null || string.Equals(NormalizeCwd(a), NormalizeCwd(b), StringComparison.OrdinalIgnoreCase);

    static int CompareForDisplay(SessionSnapshot a, SessionSnapshot b)
    {
        var byState = ((int)a.RollupState).CompareTo((int)b.RollupState);
        if (byState != 0) return byState;
        return b.LastActivityUtc.CompareTo(a.LastActivityUtc);
    }

    static string? ReadTranscriptCwd(string transcriptPath)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(firstLine);
            return doc.RootElement.TryGetProperty("cwd", out var cwdElement)
                ? cwdElement.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    static IReadOnlyList<SubagentSnapshot> EnumerateSubagents(string slugDir, string sessionId)
    {
        var subagentsDir = Path.Combine(slugDir, sessionId, "subagents");
        if (!Directory.Exists(subagentsDir)) return Array.Empty<SubagentSnapshot>();

        var files = Directory.GetFiles(subagentsDir, "agent-*.jsonl", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return Array.Empty<SubagentSnapshot>();

        var result = new List<SubagentSnapshot>(files.Length);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var agentId = name.StartsWith("agent-", StringComparison.Ordinal) ? name["agent-".Length..] : name;
            var mtime = File.GetLastWriteTimeUtc(file);
            var state = SessionStateClassifier.Classify(stateEntry: null, file);
            result.Add(new SubagentSnapshot(agentId, file, new DateTimeOffset(mtime, TimeSpan.Zero), state));
        }
        return result;
    }
}
