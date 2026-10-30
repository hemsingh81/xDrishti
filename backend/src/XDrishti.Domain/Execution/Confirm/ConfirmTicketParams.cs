namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The parameters of one confirm ticket (spec P12-08 criterion 2): everything the owner's card says, fixed when the ticket is issued. Entries are BUY only until P12-13; the limit is the entry reference, the stop and target ride with it as one
/// super order. <see cref="ApprovedAt"/> is kept to the minute and <see cref="ExpiresAt"/> to the second, which is what the text shows, so the text is a one-to-one image of these fields.
/// </summary>
public sealed record ConfirmTicketParams(
    string Side,
    int Quantity,
    string Symbol,
    decimal Limit,
    decimal Stop,
    decimal Target,
    string Broker,
    string AccountName,
    DateTimeOffset ApprovedAt,
    DateTimeOffset ExpiresAt,
    bool Canary)
{
    /// <summary>The loss per share if the stop fills at its price (gaps and slippage can lose more).</summary>
    public decimal StopDistance => Limit - Stop;

    public decimal Notional => decimal.Round(Quantity * Limit, 2, MidpointRounding.AwayFromZero);

    /// <summary>The worst case at the stop: quantity times the stop distance.</summary>
    public decimal RiskAtStop => decimal.Round(Quantity * StopDistance, 2, MidpointRounding.AwayFromZero);
}
