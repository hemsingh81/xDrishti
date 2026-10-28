using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.LiveTrading;

/// <summary>One poll of the day's trade book (and, when its switch is on, the order book) of every manual-live account, or of one. A manual poll (the owner's CLI) may run outside the window; the service never does.</summary>
public sealed record CaptureLiveSessionCommand(Guid? AccountId, bool Manual) : ICommand;

/// <summary>What a poll did, counts and codes only.</summary>
public sealed record LiveCaptureOutcome(int Accounts, int Ok, int Failed, int FillsNew, int OrdersNew, string Summary);

/// <summary>Settle the sessions of the manual-live accounts: charges and agreement from the history read, then the one outcome of every approved ticket. The date is one session (not after today), or null for the last seven days.</summary>
public sealed record SettleLiveSessionCommand(DateOnly? Date, Guid? AccountId) : ICommand;

/// <summary>What a settle did, counts and codes only (the agreement of the history read with the trade book is in the summary).</summary>
public sealed record LiveSettleOutcome(int Sessions, int OutcomesWritten, int Pending, int ChargesWritten, int Failed, string Summary);
