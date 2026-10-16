namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>One line of Dhan's ledger (ADR 0025): the voucher date (India), its number and description, the free-text narration, the exchange, the signed amount (credit − debit, rupees) and Dhan's running balance when given.</summary>
public sealed record BrokerLedgerRow(DateOnly Date, string VoucherNo, string Description, string Narration, string? Exchange, decimal Amount, decimal? RunningBalance);
