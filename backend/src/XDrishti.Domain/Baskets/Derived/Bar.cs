namespace XDrishti.Domain.Baskets.Derived;

/// <summary>One daily bar, already adjusted for corporate actions by the caller. Daily arrays are sorted by date ascending.</summary>
public readonly record struct Bar(DateOnly Date, double Open, double High, double Low, double Close, double Volume);
