namespace XDrishti.Domain.Research;

/// <summary>
/// What a costed run charged with (ADR 0042, D14): the sheet's id and rates hash, its canonical text without the note <c>checked</c> (so that the record can be recomputed), the note itself, the simulated capital, the sizing profile and
/// <c>max_cost_R</c>. Null in a run record that is gross. The run key reads the id, the rates hash, the capital, the profile numbers and <c>max_cost_R</c>.
/// </summary>
/// <param name="SheetId">The id of the sheet.</param>
/// <param name="RatesSha256"><see cref="CostSheet.RatesSha256"/>.</param>
/// <param name="SheetJson">The canonical text of the sheet without <c>checked</c>.</param>
/// <param name="Checked">What the sheet was checked against; null when unchecked.</param>
/// <param name="Capital">The simulated capital in rupees.</param>
/// <param name="Profile">The sizing profile with the numbers used.</param>
/// <param name="MaxCostR">The largest charge in R a kept trade has.</param>
public sealed record CostRecord(string SheetId, string RatesSha256, string SheetJson, string? Checked, decimal Capital, SizingProfile Profile, decimal MaxCostR)
{
    /// <summary>The record of the context a run was costed with.</summary>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    public static CostRecord Of(CostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sheet = context.Sheet;
        return new CostRecord(sheet.Id, sheet.RatesSha256, sheet.CanonicalJsonWithoutChecked(), sheet.Checked, context.Capital, context.Profile, context.MaxCostR);
    }
}
