namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An instrument of the master list that open holdings hold and that Dhan lists in the NSE equity segment, with the security id to quote it by.</summary>
public sealed record QuoteTarget(Guid InstrumentId, string SecurityId);

/// <summary>
/// What the open holdings ask to be quoted: <see cref="OpenSecurities"/> distinct securities held (open holdings of the same instrument in several accounts count once; one that is not in the master list counts by its own
/// exchange and security id) and the <see cref="Nse"/> ones among them that can be quoted. The difference are the securities that are not on the NSE (BSE only, retired, not mapped).
/// </summary>
public sealed record QuoteTargets(int OpenSecurities, IReadOnlyList<QuoteTarget> Nse);

/// <summary>A usable quote for the holdings of one instrument. The price is positive; the previous close is null when there is none to take.</summary>
public sealed record HoldingQuote(Guid InstrumentId, decimal LastPrice, decimal? PreviousClose);

/// <summary>The portfolio module's side of the quote poll (ADR 0019 §2, §3): which securities to quote and writing the answer into the holdings.</summary>
public interface IHoldingQuotes
{
    /// <summary>The securities of the open holdings of every active Portfolio account (the accounts that <see cref="IHoldingQueries"/> reads without an account filter).</summary>
    ValueTask<QuoteTargets> ListTargetsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the quotes into every <b>open</b> holding of each quoted instrument, across accounts, in one statement: last price, previous close (kept when the quote has none), <paramref name="at"/> as the
    /// price time and source <c>quote</c> (the rules of <c>Holding.ApplyQuote</c>). Closed holdings and instruments without a quote are untouched. Returns the number of holdings updated. Executes at once (it is
    /// not part of a unit of work).
    /// </summary>
    ValueTask<int> ApplyAsync(IReadOnlyList<HoldingQuote> quotes, DateTimeOffset at, CancellationToken cancellationToken);
}
