namespace XDrishti.Domain.Options.Remark;

/// <summary>An expiry date and where it came from: a stored contract (<see cref="RemarkFates.ExpiryContract"/>) or the derived weekday rule (<see cref="RemarkFates.ExpiryDerived"/>, unconfirmed).</summary>
public sealed record ExpiryDate(DateOnly Date, bool FromContract)
{
    public string Label => FromContract ? RemarkFates.ExpiryContract : RemarkFates.ExpiryDerived;
}
