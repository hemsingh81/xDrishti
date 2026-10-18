namespace XDrishti.Api.Simulate;

/// <summary>The two fill modes the verb exposes (spec P3-04b2, D1); no other fill number is a flag.</summary>
internal enum FillsMode
{
    /// <summary><c>FillRules.Default</c>: the entry limit of 0.1 % and the slippage of doc 16.</summary>
    Modelled,

    /// <summary>The same entry limit with no slippage, which also turns off the tick rounding of fills (<c>FillBasis.Exact</c>): for the hand recomputation of P3-09.</summary>
    NoSlippage,
}
