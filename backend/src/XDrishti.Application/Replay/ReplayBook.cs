using XDrishti.Domain.Paper;

namespace XDrishti.Application.Replay;

/// <summary>
/// The replay's book (SR-01): the trades the replay has booked. Everything a session reads from it goes through <see cref="LedgerAsOf"/> and the paper ledger (<see cref="PaperLedger"/>), so equity, open risk and the realised series have no second implementation and the book at S
/// uses only what is known at S. In memory only: nothing here is stored.
/// </summary>
internal sealed class ReplayBook(decimal capital)
{
    /// <summary>The one synthetic replay account.</summary>
    public static readonly Guid AccountId = new("00000000-0000-7000-8000-0000000005a1");

    private readonly List<ReplayTrade> _trades = [];

    /// <summary>The starting capital.</summary>
    public decimal Capital { get; } = capital;

    /// <summary>Every trade booked, in booking order.</summary>
    public IReadOnlyList<ReplayTrade> Trades => _trades;

    /// <summary>Books a trade.</summary>
    public void Add(ReplayTrade trade) => _trades.Add(trade);

    /// <summary>The trades entered on or before <paramref name="session"/>, with an exit only when it is on or before it.</summary>
    public IReadOnlyList<LedgerTrade> LedgerAsOf(DateOnly session) => [.. _trades.Where(t => t.EntrySession <= session).Select(t => t.LedgerAsOf(session, AccountId))];

    /// <summary>The ledger figures at the close of <paramref name="session"/>.</summary>
    public LedgerFigures FiguresAsOf(DateOnly session) => PaperLedger.Compute(Capital, LedgerAsOf(session), session);

    /// <summary>The trades open at the close of <paramref name="session"/>: entered by then, not exited by then.</summary>
    public IReadOnlyList<ReplayTrade> OpenAt(DateOnly session) => [.. _trades.Where(t => t.EntrySession <= session && (t.ExitSession is null || t.ExitSession > session))];

    /// <summary>The trades the replay has not found an exit for yet and that were entered by <paramref name="session"/>.</summary>
    public IReadOnlyList<ReplayTrade> Unexited(DateOnly session) => [.. _trades.Where(t => t.Exit is null && t.EntrySession <= session)];
}
