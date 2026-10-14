using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The checks every broker history use case shares (ADR 0025): the account exists and has the Portfolio role on, the start date the owner chose is a valid date that leaves no hole after the dates already read, and the account is connected to Dhan with a token that has not expired
/// (<c>portfolio.token_expired</c> when it ran out, <c>portfolio.not_connected</c> when there is none). Failures are results with stable codes.
/// </summary>
internal sealed class BrokerHistoryAccess(HoldingsFetcher fetcher, IBrokerSyncStore runs, TimeProvider clock)
{
    public ValueTask<Result<BrokerAccount>> AccountAsync(Guid accountId, CancellationToken cancellationToken) => fetcher.AccountAsync(accountId, cancellationToken);

    /// <summary>
    /// The owner's start date must be a plausible trade date (not in the future, not before 1990-01-02) and must not leave a hole: the watermark of a kind says every date up to it was read, so a start later than the day after it of either kind would skip
    /// the dates in between for good (<c>400 portfolio.invalid</c>, field <c>from</c>). A kind that was never read while the other was has no watermark to go by, but its history starts at the account's first import date: the start may not be later than that
    /// either, or the chosen date would become where that kind begins. An earlier start only reads dates again, which stores nothing twice. An account where <b>neither</b> kind was read may start at any date.
    /// </summary>
    public async ValueTask<Result> CheckFromAsync(BrokerAccount account, DateOnly? from, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (from is not { } date)
        {
            return Result.Success();
        }

        var errors = new FieldErrors();
        TradeRules.CheckDate(errors, "from", date, IndiaTime.Today(clock.GetUtcNow()));
        if (errors.HasErrors)
        {
            return Result.Failure(PortfolioErrors.Invalid(errors));
        }

        var trades = await runs.GetWatermarkAsync(account.Id, BrokerSyncKind.Trades, cancellationToken);
        var ledger = await runs.GetWatermarkAsync(account.Id, BrokerSyncKind.Ledger, cancellationToken);
        var latestStart = (trades, ledger) switch
        {
            ({ } t, { } l) => (t < l ? t : l).AddDays(1),
            ({ } t, null) => Earlier(t.AddDays(1), account.Portfolio.ImportDate),
            (null, { } l) => Earlier(l.AddDays(1), account.Portfolio.ImportDate),
            _ => (DateOnly?)null,
        };
        return latestStart is { } latest && date > latest ? Result.Failure(PortfolioErrors.HistoryStartTooLate(latest)) : Result.Success();
    }

    private static DateOnly Earlier(DateOnly a, DateOnly b) => a < b ? a : b;

    /// <summary>The account's saved Dhan token when the connection is usable.</summary>
    public async ValueTask<Result<SecretValue>> TokenAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return await fetcher.StatusAsync(account.Id, cancellationToken) switch
        {
            ConnectionStatus.Connected => await fetcher.TokenAsync(account.Id, cancellationToken),
            ConnectionStatus.Expired => Result.Failure<SecretValue>(PortfolioErrors.TokenExpired),
            _ => Result.Failure<SecretValue>(PortfolioErrors.NotConnected),
        };
    }
}
