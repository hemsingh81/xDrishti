namespace XDrishti.Domain.Options.Remark;

/// <summary>What the held strike gives at one bar time, in paise (P14-08): the fate when it cannot be priced (no bar and no fallback, or an unusable key-session), else the open, the low, the high and the price to exit at (the open, or the close of a bar the position leaves at the close of), and whether it is the Black-76 fallback.</summary>
public readonly record struct BarPremium(string? Fate, int Open, int Low, int High, int Px, bool Model);
