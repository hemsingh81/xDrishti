namespace XDrishti.Domain.Confidence;

/// <summary>Which trade count is T in the deflated Sharpe ratio (CF-03, review fix 3).</summary>
public enum DeflationBasis
{
    /// <summary>The effective trades of CF-01 (the default); unavailable when CF-01 gave none.</summary>
    NEff = 0,

    /// <summary>The raw trade count, with the effective number shown beside it.</summary>
    Raw = 1,
}
