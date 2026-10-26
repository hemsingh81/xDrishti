using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>One paper account's derived ledger: its trades, the figures, the open positions with marks, and the instrument of each open symbol.</summary>
/// <param name="Account">The account.</param>
/// <param name="Trades">Its stored trades in the epoch.</param>
/// <param name="Figures">The derived figures.</param>
/// <param name="Positions">The open positions with their marks.</param>
/// <param name="Instruments">The instrument id of each open symbol that has a stored daily bar.</param>
internal sealed record AccountLedger(PaperAccountInput Account, IReadOnlyList<LedgerTrade> Trades, LedgerFigures Figures, IReadOnlyList<PaperPosition> Positions, IReadOnlyDictionary<string, Guid> Instruments);
