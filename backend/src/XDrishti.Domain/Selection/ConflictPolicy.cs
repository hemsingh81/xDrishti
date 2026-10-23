namespace XDrishti.Domain.Selection;

/// <summary>What to do with a symbol that has an eligible BUY and an eligible SELL (doc 10 section 3).</summary>
public enum ConflictPolicy
{
    /// <summary>Keep the stronger side when it leads by more than the margin; otherwise drop both.</summary>
    KeepStrongerOrDropBoth,

    /// <summary>Always drop both.</summary>
    DropBoth,
}
