namespace XDrishti.Domain.Options.Remark;

/// <summary>An expiry date seen in <c>options.contracts</c> for an underlying: its date and kind (<c>W</c> weekly, <c>M</c> monthly).</summary>
public sealed record ContractExpiry(DateOnly Date, char Flag);
