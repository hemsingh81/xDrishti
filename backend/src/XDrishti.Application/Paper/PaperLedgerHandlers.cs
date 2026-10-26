using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>What the four read handlers share: the epoch, the last closed session, the calendar and the paper accounts.</summary>
internal sealed class PaperLedgerContext(IPaperLedgerData data, PaperLedgerService ledgers, ITradingCalendar calendars, TimeProvider time)
{
    public async ValueTask<Result<Resolved>> ResolveAsync(long? epochId, Guid? accountId, CancellationToken cancellationToken)
    {
        var epoch = await data.EpochAsync(epochId, cancellationToken);
        if (epochId is not null && epoch is null)
        {
            return Result.Failure<Resolved>(PaperErrors.NotFound("paper epoch with this id"));
        }

        var accounts = await ledgers.PaperAccountsAsync(cancellationToken);
        if (accountId is { } id && accounts.All(a => a.AccountId != id))
        {
            return Result.Failure<Resolved>(PaperErrors.NotFound("paper account with this id (it is unknown, or its mode is not paper)"));
        }

        var calendar = await calendars.GetAsync(cancellationToken);
        return Result.Success(new Resolved(epoch, accounts, calendar, calendar.LastClosedSession(time.GetUtcNow())));
    }

    public ValueTask<IReadOnlyList<AccountLedger>> BuildAsync(Resolved r, CancellationToken cancellationToken) =>
        ledgers.BuildAsync(r.Accounts, r.Epoch, r.LastClosed, r.Calendar, cancellationToken);

    /// <summary>The resolved inputs of a read.</summary>
    /// <param name="Epoch">The epoch, or null before the first capture.</param>
    /// <param name="Accounts">The paper accounts now.</param>
    /// <param name="Calendar">The trading calendar.</param>
    /// <param name="LastClosed">The last closed session.</param>
    public sealed record Resolved(EpochRow? Epoch, IReadOnlyList<PaperAccountInput> Accounts, TradingCalendar Calendar, DateOnly LastClosed);
}

/// <summary>The paper accounts with their derived ledgers (spec P10-02). Read only.</summary>
internal sealed class GetPaperAccountsHandler(PaperLedgerContext context, ILogger<GetPaperAccountsHandler> logger) : IQueryHandler<GetPaperAccountsQuery, Result<PaperAccountList>>
{
    public async ValueTask<Result<PaperAccountList>> HandleAsync(GetPaperAccountsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var resolved = await context.ResolveAsync(query.EpochId, null, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<PaperAccountList>(resolved.Error);
        }

        var r = resolved.Value;
        var built = await context.BuildAsync(r, cancellationToken);
        var open = built.Sum(a => a.Figures.OpenCount);
        var closed = built.Sum(a => a.Figures.ClosedCount);
        PaperLog.LedgerRead(logger, built.Count, open, closed);
        return Result.Success(new PaperAccountList(PaperWording.Basis, [.. built.Select(a => PaperViewMapper.Account(a, r.Epoch, r.LastClosed))]));
    }
}

/// <summary>The open positions and closed trades of one paper account (spec P10-02). Read only.</summary>
internal sealed class GetPaperPositionsHandler(PaperLedgerContext context) : IQueryHandler<GetPaperPositionsQuery, Result<PaperPositionList>>
{
    public async ValueTask<Result<PaperPositionList>> HandleAsync(GetPaperPositionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var resolved = await context.ResolveAsync(query.EpochId, query.AccountId, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<PaperPositionList>(resolved.Error);
        }

        var r = resolved.Value;
        var one = (await context.BuildAsync(r with { Accounts = [.. r.Accounts.Where(a => a.AccountId == query.AccountId)] }, cancellationToken))[0];
        var closed = PaperLedger.Closed(one.Trades).AsEnumerable().Reverse()
            .Select(t => new PaperClosedTradeView(t.Symbol, t.Side, t.Qty, t.EntryAt, t.Exit!.ExitAt, t.Exit.GrossPnl, t.Exit.Charges, t.Exit.Result, t.Exit.RSim))
            .ToList();
        return Result.Success(new PaperPositionList(PaperWording.Basis, PaperWording.Limits, query.AccountId, r.LastClosed, [.. one.Positions.Select(p => PaperViewMapper.Position(p))], closed));
    }
}

/// <summary>The realised-equity line of one paper account (spec P10-02). Read only.</summary>
internal sealed class GetPaperEquityHandler(PaperLedgerContext context, IPaperLedgerData data) : IQueryHandler<GetPaperEquityQuery, Result<PaperEquityView>>
{
    private const int MaxPoints = 400;

    public async ValueTask<Result<PaperEquityView>> HandleAsync(GetPaperEquityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var resolved = await context.ResolveAsync(query.EpochId, query.AccountId, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<PaperEquityView>(resolved.Error);
        }

        var r = resolved.Value;
        if (query.From is { } f && query.To is { } t && f > t)
        {
            return Result.Failure<PaperEquityView>(PaperErrors.Invalid("from", "The first day is after the last day."));
        }

        var account = r.Accounts.First(a => a.AccountId == query.AccountId);
        if (r.Epoch is null)
        {
            return Result.Success(new PaperEquityView(PaperWording.Basis, PaperWording.Limits, query.AccountId, null, []));
        }

        var first = query.From is { } from && from > r.Epoch.StartedOn ? from : r.Epoch.StartedOn;
        var last = query.To is { } to && to < r.LastClosed ? to : r.LastClosed;
        var dates = new List<DateOnly>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (r.Calendar.IsTradingDay(day))
            {
                dates.Add(day);
            }
        }

        if (dates.Count > MaxPoints)
        {
            return Result.Failure<PaperEquityView>(PaperErrors.Invalid("from", $"The range holds more than {MaxPoints} sessions."));
        }

        var trades = await data.TradesAsync(r.Epoch.Id, query.AccountId, cancellationToken);
        var points = PaperLedger.RealisedSeries(account.Capital, trades, dates)
            .Select(p => new PaperEquityPoint(p.Date, p.Result, PaperViewMapper.Money(p.Equity, "the account has no capital figure")))
            .ToList();
        return Result.Success(new PaperEquityView(PaperWording.Basis, PaperWording.Limits, query.AccountId, r.Epoch.Id, points));
    }
}

/// <summary>Compares the derived ledger with sums read straight from the stored rows (spec P10-02, criterion 4). Read only.</summary>
internal sealed class ReconcilePaperHandler(PaperLedgerContext context, IPaperLedgerData data, ILogger<ReconcilePaperHandler> logger) : IQueryHandler<ReconcilePaperQuery, Result<PaperReconcileView>>
{
    public async ValueTask<Result<PaperReconcileView>> HandleAsync(ReconcilePaperQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var resolved = await context.ResolveAsync(query.EpochId, query.AccountId, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<PaperReconcileView>(resolved.Error);
        }

        var r = resolved.Value;
        if (r.Epoch is null)
        {
            return Result.Success(new PaperReconcileView(PaperWording.Basis, query.AccountId, null, "ok", []));
        }

        var one = (await context.BuildAsync(r with { Accounts = [.. r.Accounts.Where(a => a.AccountId == query.AccountId)] }, cancellationToken))[0];
        var totals = await data.TotalsAsync(r.Epoch.Id, query.AccountId, cancellationToken);
        var differences = new List<string>();
        if (totals.ExitResult != one.Figures.Result)
        {
            differences.Add($"the stored exits sum to {totals.ExitResult:0.00} but the ledger's realised result is {one.Figures.Result:0.00}");
        }

        if (totals.EntriesWithoutExit != one.Figures.OpenCount)
        {
            differences.Add($"{totals.EntriesWithoutExit} stored entries have no exit but the ledger shows {one.Figures.OpenCount} open positions");
        }

        if (totals.ExitBeforeEntry > 0)
        {
            differences.Add($"{totals.ExitBeforeEntry} stored exits are before their entry");
        }

        var known = r.Accounts.Select(a => a.AccountId).ToHashSet();
        foreach (var id in (await data.EntryAccountsAsync(r.Epoch.Id, cancellationToken)).Where(id => !known.Contains(id)))
        {
            differences.Add($"entries exist in this epoch for account {id}, which is not a paper account now");
        }

        if (differences.Count > 0)
        {
            PaperLog.ReconcileDifferences(logger, differences.Count);
        }

        return Result.Success(new PaperReconcileView(PaperWording.Basis, query.AccountId, r.Epoch.Id, differences.Count == 0 ? "ok" : "differences", differences));
    }
}

/// <summary>Turns ledgers into views. Every figure that needs the capital or a mark is a <see cref="MetricView"/>: a state and, only for a value, a number.</summary>
internal static class PaperViewMapper
{
    private const string NoCapital = "the account has no capital figure";

    public static MetricView Money(decimal? value, string unavailableReason) =>
        value is { } v ? new MetricView("value", (double)v, null) : new MetricView("unavailable", null, unavailableReason);

    public static PaperAccountView Account(AccountLedger a, EpochRow? epoch, DateOnly lastClosed)
    {
        var f = a.Figures;
        var unrealised = a.Positions.Count == 0
            ? new MetricView("empty", null, "no open position")
            : Money(PaperLedger.TotalUnrealised(a.Positions), "a mark is unavailable, so the total is not the sum of the others");
        var sheets = a.Trades.Where(t => t.Exit?.ChargeSheetId is not null).Select(t => t.Exit!.ChargeSheetId!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new PaperAccountView(
            a.Account.AccountId, a.Account.Name, epoch?.Id, a.Account.Capital, a.Account.CapitalSource,
            new PaperRealisedView(f.Gross, f.Charges, f.Result, f.Basis),
            Money(f.Equity, NoCapital),
            Money(f.FundsAvailable, NoCapital),
            Money(f.OpenRiskPct, NoCapital),
            f.OpenCount, unrealised, f.LastSessionResult, Money(f.LastSessionPct, NoCapital),
            new PaperDrawdownView(f.MaxDrawdown, Money(f.MaxDrawdownPct, NoCapital)),
            new PaperChargeSheetView(sheets.Count == 0 ? null : string.Join(", ", sheets), null),
            new PaperEvidenceView(f.ClosedCount, f.OpenCount, epoch?.StartedOn, lastClosed),
            PaperWording.Limits);
    }

    public static PaperPositionView Position(PaperPosition p) =>
        new(
            p.Trade.Symbol, p.Trade.Side, p.Trade.Qty, p.Trade.EntryAt, p.Trade.EntryPrice, p.Trade.StopPrice,
            new PaperMarkView(p.Mark.State, p.Mark.Price, p.Mark.AsOf, p.Mark.Reason),
            Money(p.Unrealised, p.Mark.Reason ?? "the mark is unavailable"),
            p.Trade.RiskAmount, Money(p.RiskPctOfEquity, NoCapital), p.HeldSessions);
}
