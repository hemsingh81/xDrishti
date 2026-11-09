namespace XDrishti.Application.SettingsCatalog;

/// <summary>Who sees a setting (ADR 0242): <c>Essential</c> (at most eight), <c>Advanced</c> (one named level), <c>Internal</c> (never on the settings screen).</summary>
public enum SettingTier
{
    Essential,
    Advanced,
    Internal,
}

/// <summary>The direction in which a setting is safer. <c>None</c> means no direction is declared, and then a change either way counts as loosening (ADR 0242).</summary>
public enum SettingSafer
{
    None,
    Higher,
    Lower,
}

/// <summary>Where the value of a setting lives: a configuration key, an owner switch stored in the database, or a whole configuration section (a group of internal keys, no value shown).</summary>
public enum SettingKind
{
    Configuration,
    Stored,
    Section,
}

/// <summary>
/// The one owner record of a setting (AU-08, ADR 0242): its key, label, tier, default, outer numeric bounds, safer direction and whether learning may ever propose it.
/// <c>Min</c> and <c>Max</c> are inclusive outer bounds; stricter cross-field rules stay in the existing validators, so a descriptor never narrows what the host accepts today.
/// </summary>
public sealed record SettingDescriptor(
    string Key,
    string Label,
    SettingTier Tier,
    SettingKind Kind,
    string? Default,
    decimal? Min,
    decimal? Max,
    SettingSafer Safer,
    bool Learnable)
{
    /// <summary>Whether moving the value from <paramref name="from"/> to <paramref name="to"/> loosens a gate. A key with no declared direction loosens in both directions.</summary>
    public bool IsLoosening(decimal from, decimal to) => Safer switch
    {
        SettingSafer.Higher => to < from,
        SettingSafer.Lower => to > from,
        _ => to != from,
    };

    /// <summary>Whether the descriptor carries numeric bounds.</summary>
    public bool HasRange => Min is not null || Max is not null;

    /// <summary>Whether <paramref name="value"/> lies inside the bounds (a descriptor without bounds accepts any value).</summary>
    public bool InRange(decimal value) => (Min is null || value >= Min) && (Max is null || value <= Max);
}
