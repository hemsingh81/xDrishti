namespace XDrishti.Domain.Learning;

/// <summary>
/// How a cash signal is expressed as an instrument (P14-06, ADR 0193): the eighth part of a cell key. A closed set, stored by name. Only <see cref="Cash"/> reaches selection, planning, paper, live and execution; every other value is research
/// only (P14-00 risk 8).
/// </summary>
public enum Expression
{
    /// <summary>The share itself: every simulator trade, and every cell stored before P14-06.</summary>
    Cash,

    /// <summary>A future.</summary>
    Future,

    /// <summary>A bought call or put.</summary>
    OptionBuy,

    /// <summary>A debit spread.</summary>
    DebitSpread,

    /// <summary>A credit spread.</summary>
    CreditSpread,

    /// <summary>An iron condor.</summary>
    IronCondor,

    /// <summary>A pair of shares.</summary>
    Pair,

    /// <summary>A cash-and-future basis trade.</summary>
    Basis,
}
