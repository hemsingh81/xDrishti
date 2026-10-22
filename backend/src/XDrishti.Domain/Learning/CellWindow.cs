namespace XDrishti.Domain.Learning;

/// <summary>The windows of doc 09 layer 1: every trade known at the as-of date, the last 250 calendar days, the last 60 calendar days.</summary>
public enum CellWindow
{
    /// <summary>Every trade known at the as-of date.</summary>
    All,

    /// <summary>Exits in the 250 days up to and including the as-of date.</summary>
    Days250,

    /// <summary>Exits in the 60 days up to and including the as-of date.</summary>
    Days60,
}
