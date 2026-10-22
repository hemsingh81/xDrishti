namespace XDrishti.Domain.Learning;

/// <summary>One named decision-time feature of the catalog (P6-04, ADR 0066).</summary>
/// <param name="Name">The snake_case name; unique in the catalog.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="RequiredByDefault">A training row with this feature unknown is dropped (and counted) unless the caller asks for another rule.</param>
public sealed record TradeFeatureDef(string Name, TradeFeatureSource Source, bool RequiredByDefault);
