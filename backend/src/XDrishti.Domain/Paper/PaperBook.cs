using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Paper;

/// <summary>The paper book for the plan (spec P10-02): every open paper position, each paper account's figures and the counted trades for the equity throttle. Derived from the stored paper trades, complete up to the last closed session.</summary>
/// <param name="Positions">Open positions of the accounts that have a capital figure (<see cref="BookPosition.RiskPct"/> is of that account's equity).</param>
/// <param name="Accounts">The figures per paper account.</param>
/// <param name="Taken">One taken trade per counted ticket (primary, closed, current epoch), counted once however many accounts it was sized for.</param>
/// <param name="Notes">Honest-limit sentences for the plan.</param>
public sealed record PaperBook(IReadOnlyList<BookPosition> Positions, IReadOnlyList<PaperAccountBook> Accounts, IReadOnlyList<TakenTrade> Taken, IReadOnlyList<string> Notes) : PaperBookRead;
