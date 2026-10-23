namespace XDrishti.Domain.Checklist;

/// <summary>Per-check policy of a failure. Defaults follow doc 16 (<c>data_quality: block_with_confirm, holdings_conflict: warn, sector_limit: warn</c>); every other check blocks with confirmation (the safe side).</summary>
/// <param name="Overrides">Policy by check code; absent codes use the defaults.</param>
public sealed record ChecklistPolicy(IReadOnlyDictionary<string, CheckPolicy>? Overrides = null)
{
    /// <summary>The policy of a check.</summary>
    public CheckPolicy Of(string code) =>
        Overrides is not null && Overrides.TryGetValue(code, out var p) ? p : code switch
        {
            ChecklistCodes.HoldingsConflict or ChecklistCodes.SectorExposure => CheckPolicy.Warn,
            _ => CheckPolicy.BlockWithConfirm,
        };
}
