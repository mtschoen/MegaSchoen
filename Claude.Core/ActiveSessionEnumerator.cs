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

        if (candidates.Count == 0) return new List<SessionSnapshot>();

        // Shared-cwd cap: at most procs.Count sessions are alive here. Keep
        // the freshest by last write (relative rank — NOT a time cutoff).
        var kept = candidates.Values
            .OrderByDescending(c => c.LastWriteUtc)
            .Take(procs.Count)
            .ToList();

        var slots = AssignProcesses(kept, procs);

        var result = new List<SessionSnapshot>(slots.Count);
        foreach (var slot in slots)
        {
            var candidate = slot.Candidate;
            var entry = stateBySessionId.TryGetValue(candidate.Id, out var e) ? e : null;
            var state = SessionStateClassifier.Classify(entry, candidate.TranscriptPath);
            var process = slot.Process;
            var title = process?.Title ?? "";
            var subagents = EnumerateSubagents(slugDir, candidate.Id);

            result.Add(new SessionSnapshot(
                SessionId: candidate.Id,
                Cwd: cwd,
                TranscriptPath: candidate.TranscriptPath,
                LastActivityUtc: new DateTimeOffset(candidate.LastWriteUtc, TimeSpan.Zero),
                State: state,
                PendingMessage: entry?.Message,
                Window: process?.Window ?? WindowToken.Null,
                WindowTitle: string.IsNullOrEmpty(title) ? null : title,
                Subagents: subagents,
                SshClientPort: process?.SshClientPort));
        }
        return result;
    }

    // One kept candidate plus the live process attributed to it (null until a
    // pass attaches one). Mutated in place across the three assignment passes.
    sealed class Slot(Candidate candidate)
    {
        public Candidate Candidate { get; } = candidate;
        public ClaudeWindow? Process { get; set; }
    }

    // Attributes a live process to each kept candidate (leaving its Slot.Process
    // null when none can be attached) in three passes, each attaching only when
    // the session-to-process association is unambiguous (focusing the wrong
    // terminal is worse than a disabled Focus button):
    //   1. Confident start-time match: a live process whose StartTime is within
    //      tolerance of the transcript's creation time (MatchProcessByStartTime).
    //   2. Causal forced-move: a process can only have written a transcript that
    //      was created at or after the process started, so a candidate's owner
    //      must be a free process with StartTime <= the transcript's creation
    //      time. When a candidate has exactly one such free process it is
    //      unambiguously the owner — attach it even though the start-time gap
    //      exceeded Pass 1's tolerance. This is the common shared-cwd case
    //      (several sessions in one repo, each with a slow first prompt, so
    //      every transcript is created well after its process started). The pass
    //      iterates because attaching one candidate can make a peer's choice
    //      unique in turn; it refuses while two or more free processes remain
    //      causally plausible for the same candidate.
    //   3. Last-one-standing elimination: when exactly one candidate and exactly
    //      one live process remain unattributed, their pairing is unambiguous by
    //      elimination — attach it regardless of causality. This covers a
    //      resumed session, whose old transcript predates its (re)started
    //      process so Pass 2 cannot apply, in its cwd's only terminal.
    // The start-time match (Pass 1) misses for rig-verified reasons:
    //  - Linux remote: .NET's file creation time tracks mtime, so an active
    //    session's transcript drifts past the 30s tolerance (Linux has no window
    //    anyway, but the piggybacked SSH client port must still thread through).
    //  - Windows: a freshly started claude writes no transcript until the first
    //    prompt, so either the freshest OLD transcript surfaces (resume) or the
    //    new transcript is created well after the process started (slow first
    //    prompt) — either way its creation time lands past the tolerance.
    static List<Slot> AssignProcesses(List<Candidate> kept, List<ClaudeWindow> procs)
    {
        var slots = kept.Select(c => new Slot(c)).ToList();
        var usedProcesses = new HashSet<uint>();

        // Pass 1: confident start-time match.
        foreach (var slot in slots)
        {
            if (MatchProcessByStartTime(procs, slot.Candidate.CreationUtc, usedProcesses) is { } process)
            {
                Attach(slot, usedProcesses, process);
            }
        }

        // Pass 2: causal forced-move (iterate until no candidate is newly forced).
        bool progress;
        do
        {
            progress = false;
            foreach (var slot in slots)
            {
                if (slot.Process is not null) continue;
                if (SoleCausalProcess(procs, slot.Candidate.CreationUtc, usedProcesses) is { } owner)
                {
                    Attach(slot, usedProcesses, owner);
                    progress = true;
                }
            }
        } while (progress);

        // Pass 3: last-one-standing elimination.
        var unmatched = slots.Where(s => s.Process is null).ToList();
        if (unmatched.Count == 1)
        {
            var freeProcesses = procs.Where(p => !usedProcesses.Contains(p.ProcessId)).ToList();
            if (freeProcesses.Count == 1)
            {
                Attach(unmatched[0], usedProcesses, freeProcesses[0]);
            }
        }
        return slots;
    }

    static void Attach(Slot slot, HashSet<uint> usedProcesses, ClaudeWindow process)
    {
        slot.Process = process;
        usedProcesses.Add(process.ProcessId);
    }

    // The single free process that could causally own a transcript created at
    // creationUtc (StartTime <= creationUtc), or null when none qualifies or two
    // or more do (ambiguous — refuse to guess).
    static ClaudeWindow? SoleCausalProcess(
        List<ClaudeWindow> procs, DateTime creationUtc, HashSet<uint> usedProcesses)
    {
        ClaudeWindow? sole = null;
        foreach (var p in procs)
        {
            if (usedProcesses.Contains(p.ProcessId)) continue;
            if (p.StartTimeUtc.UtcDateTime > creationUtc) continue;
            if (sole is not null) return null; // ambiguous
            sole = p;
        }
        return sole;
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

    // Best-effort confident match: the unused live process whose StartTime is
    // closest to the transcript's creation time, within tolerance. Returns null
    // when no process is within tolerance (the caller leaves the candidate
    // unmatched for the elimination pass). Does not mutate usedProcesses — the
    // caller records the match so a windowless-but-matched process is still
    // excluded from the elimination pass.
    static ClaudeWindow? MatchProcessByStartTime(
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
        return best is { } match && bestDelta <= StartTimeMatchTolerance ? match : null;
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
