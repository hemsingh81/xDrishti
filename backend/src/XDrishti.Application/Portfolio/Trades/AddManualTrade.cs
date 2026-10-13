using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// Adds one trade typed in by the owner (ADR 0023), for a holding (<c>HoldingId</c>) or for a security of an account (<c>AccountId</c> with a <c>Symbol</c> and/or <c>Isin</c> that is an NSE share or ETF of the master list — a typing
/// mistake cannot create a trade of nothing). Validated like a CSV row: side buy or sell, the date from 1990-01-02 to today (India), a whole <c>Quantity</c> above 0, <c>Price</c> above 0, <c>Charges</c> 0 or more (default 0). Identical manual
/// trades are not deduplicated — each is its own trade — and a mistake is voided, not edited. Changes no lots: rebuild them afterwards. Audited.
/// </summary>
public sealed record AddManualTradeCommand(ManualTradeInput Input, string Actor) : ICommand;

internal sealed class AddManualTradeHandler(
    IAccountStore accounts,
    IHoldingQueries holdings,
    ITradeReferenceData reference,
    ITradeStore trades,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AddManualTradeHandler> logger) : ICommandHandler<AddManualTradeCommand, TradeView>
{
    public async ValueTask<Result<TradeView>> HandleAsync(AddManualTradeCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var errors = new FieldErrors();
        if (input is null)
        {
            errors.Add("side", "Required.");
            return Result.Failure<TradeView>(PortfolioErrors.Invalid(errors));
        }

        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        if (!TradeRules.TryParseSide(input.Side, out var side))
        {
            errors.Add("side", "Use buy or sell.");
        }

        var date = input.TradeDate ?? default;
        if (input.TradeDate is null)
        {
            errors.Add("tradeDate", "Required.");
        }
        else
        {
            TradeRules.CheckDate(errors, "tradeDate", date, today);
        }

        var qty = 0L;
        if (input.Quantity is not { } quantity)
        {
            errors.Add("quantity", "Required.");
        }
        else if (quantity != decimal.Truncate(quantity))
        {
            errors.Add("quantity", "Must be a whole number of shares.");
        }
        else if (quantity is > TradeRules.MaxQty or < long.MinValue)
        {
            errors.Add("quantity", $"At most {TradeRules.MaxQty:N0}.");
        }
        else
        {
            qty = (long)quantity;
            if (TradeRules.QtyProblem(qty) is { } qtyProblem)
            {
                errors.Add("quantity", qtyProblem);
            }
        }

        var price = 0m;
        if (input.Price is not { } rawPrice)
        {
            errors.Add("price", "Required.");
        }
        else
        {
            price = TradeRules.Round(rawPrice);
            if (TradeRules.PriceProblem(price) is { } priceProblem)
            {
                errors.Add("price", priceProblem);
            }
        }

        var charges = TradeRules.Round(input.Charges ?? 0m);
        if (TradeRules.ChargesProblem(charges) is { } chargesProblem)
        {
            errors.Add("charges", chargesProblem);
        }

        var target = await ResolveAsync(input, errors, cancellationToken);
        if (errors.HasErrors || target is null)
        {
            return Result.Failure<TradeView>(PortfolioErrors.Invalid(errors));
        }

        var account = await accounts.FindReadOnlyAsync(target.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<TradeView>(PortfolioErrors.AccountNotFound);
        }

        if (!account.Portfolio.Enabled)
        {
            return Result.Failure<TradeView>(PortfolioErrors.RoleOff);
        }

        var trade = Transaction.Create(
            account.Id,
            target.InstrumentId,
            target.Isin,
            target.Symbol,
            "NSE",
            side,
            date,
            input.TradeTime,
            qty,
            price,
            charges,
            TradeSource.Manual,
            null,
            0,
            TradeIdentity.ForManual(),
            command.Actor,
            now);
        trades.Add([trade]);
        events.Record(AccountEvent.Record(AccountEventType.TradeAdded, account.Id, $"Trade added by hand ({trade.Side.ToString().ToLowerInvariant()})", command.Actor, now));
        await unitOfWork.SaveAsync(cancellationToken);
        TradesLog.Added(logger, trade.Id, account.Slug);
        return Result.Success(TradeViews.Trade(trade));
    }

    /// <summary>The account and security the trade is for: the holding's, or the master-list share or ETF of the given account; field errors otherwise.</summary>
    private async ValueTask<ManualTarget?> ResolveAsync(ManualTradeInput input, FieldErrors errors, CancellationToken cancellationToken)
    {
        if (input.HoldingId is { } holdingId)
        {
            var holding = await holdings.GetAsync(holdingId, cancellationToken);
            if (holding is null)
            {
                errors.Add("holdingId", "The holding does not exist.");
                return null;
            }

            if (input.AccountId is { } given && given != holding.AccountId)
            {
                errors.Add("accountId", "Does not match the holding's account.");
                return null;
            }

            return new ManualTarget(holding.AccountId, holding.InstrumentId, holding.Isin, holding.TradingSymbol);
        }

        if (input.AccountId is not { } accountId || accountId == Guid.Empty)
        {
            errors.Add("accountId", "Required (or give a holdingId).");
            return null;
        }

        var symbol = TradeRules.NormaliseSymbol(input.Symbol);
        var isin = TradeRules.NormaliseIsin(input.Isin);
        if (symbol.Length == 0 && isin is null)
        {
            errors.Add("symbol", "Give the symbol or the ISIN of an NSE share or ETF.");
            return null;
        }

        if (symbol.Length > Holding.MaxSymbolLength)
        {
            errors.Add("symbol", $"At most {Holding.MaxSymbolLength} characters.");
            return null;
        }

        var found = new TradeInstruments(await reference.FindInstrumentsAsync(isin is null ? [] : [isin], symbol.Length == 0 ? [] : [symbol], cancellationToken)).Find(isin, symbol);
        if (found is null)
        {
            errors.Add("symbol", "Not an NSE share or ETF of the instrument master. Check the symbol or the ISIN.");
            return null;
        }

        return new ManualTarget(accountId, found.Id, isin ?? found.Isin, symbol.Length > 0 ? symbol : found.Symbol);
    }

    private sealed record ManualTarget(Guid AccountId, Guid? InstrumentId, string? Isin, string Symbol);
}
