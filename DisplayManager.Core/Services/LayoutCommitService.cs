using DisplayManager.Core.Models;

namespace DisplayManager.Core.Services;

/// <summary>
/// The verified-before-commit gate. A draft is committed over a real preset only after
/// it has been Tested — applied to live hardware and read back with no drift. The verified
/// stamp is tied to the exact draft content via <see cref="LayoutHasher"/>; editing after
/// a successful Test invalidates it. Apply + drift are injected so the gate is unit-testable
/// without native calls.
/// </summary>
public class LayoutCommitService
{
    readonly Func<IReadOnlyList<SavedDisplayConfig>, ApplyResult> _apply;
    readonly Func<SavedDisplayProfile, DriftReport> _compare;
    readonly DisplayProfileService _profileService;
    readonly Func<Guid, Task<bool>> _presetExists;

    public LayoutCommitService(
        Func<IReadOnlyList<SavedDisplayConfig>, ApplyResult>? apply = null,
        Func<SavedDisplayProfile, DriftReport>? compare = null,
        DisplayProfileService? profileService = null,
        Func<Guid, Task<bool>>? presetExists = null)
    {
        _apply = apply ?? (displays => DisplayManager.ApplyConfiguration(displays));
        var drift = new DisplayDriftService();
        _compare = compare ?? (profile => drift.CompareToLive(profile));
        _profileService = profileService ?? new DisplayProfileService();
        _presetExists = presetExists ?? (async id => (await _profileService.GetAllProfilesAsync()).Any(p => p.Id == id));
    }

    /// <summary>
    /// Normalize-guard → apply → read back. On no drift, stamps the draft's VerifiedHash.
    /// Throws if apply itself fails (the layout never reached hardware).
    /// </summary>
    public Task<DriftReport> TestAsync(LayoutDraft draft)
    {
        // Never apply a known-invalid layout: normalize first.
        var normalized = LayoutNormalizer.Normalize(draft.Displays);
        draft.Displays = normalized;

        var applyResult = _apply(normalized);
        if (!applyResult.Success)
        {
            throw new InvalidOperationException(
                $"Apply failed: {string.Join("; ", applyResult.Errors)}");
        }

        var report = _compare(new SavedDisplayProfile { Displays = normalized });
        draft.VerifiedHash = report.Matches ? LayoutHasher.Hash(normalized) : "";
        return Task.FromResult(report);
    }

    public bool CanCommit(LayoutDraft draft) =>
        !string.IsNullOrEmpty(draft.VerifiedHash)
        && draft.VerifiedHash == LayoutHasher.Hash(draft.Displays);

    /// <summary>
    /// Writes the verified draft into the real preset. Throws if not verified.
    /// When <paramref name="requireExisting"/> is true (the default), also throws if the
    /// target preset no longer exists in storage (deleted elsewhere since the draft was opened).
    /// </summary>
    public async Task CommitAsync(LayoutDraft draft, SavedDisplayProfile preset, bool requireExisting = true)
    {
        if (!CanCommit(draft))
        {
            throw new InvalidOperationException("Draft is not verified; cannot commit.");
        }

        if (requireExisting && !await _presetExists(preset.Id))
        {
            throw new InvalidOperationException(
                "The target preset no longer exists (deleted elsewhere). Stash your draft instead.");
        }

        preset.Displays = draft.Displays.Select(CloneConfig).ToList();
        await _profileService.SaveProfileAsync(preset);
    }

    static SavedDisplayConfig CloneConfig(SavedDisplayConfig display) => new()
    {
        MonitorName = display.MonitorName,
        EdidManufactureId = display.EdidManufactureId,
        EdidProductCodeId = display.EdidProductCodeId,
        EdidSerialNumber = display.EdidSerialNumber,
        EdidManufactureDate = display.EdidManufactureDate,
        EdidContainerId = display.EdidContainerId,
        Width = display.Width,
        Height = display.Height,
        PositionX = display.PositionX,
        PositionY = display.PositionY,
        RefreshRate = display.RefreshRate,
        Rotation = display.Rotation,
        IsPrimary = display.IsPrimary
    };
}
