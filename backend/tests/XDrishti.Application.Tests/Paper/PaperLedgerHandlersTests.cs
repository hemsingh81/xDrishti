using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Paper;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-02 criteria 1, 3, 4, 5, 9, 11 through the read handlers: the views carry the hand numbers, honest marks, the gross/after-sheet label, the reconciliation and the 404/422 answers.</summary>
public sealed class PaperLedgerHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<PaperAccountView> AccountAsync(PaperLedgerFixture f)
    {
        var list = await new GetPaperAccountsHandler(f.Context(), NullLogger<GetPaperAccountsHandler>.Instance).HandleAsync(new GetPaperAccountsQuery(null), Ct);
        list.IsSuccess.ShouldBeTrue();
        list.Value.Basis.ShouldBe(PaperWording.Basis);
        return list.Value.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_account_view_equals_the_hand_numbers_with_the_after_sheet_label()
    {
        var a = await AccountAsync(new PaperLedgerFixture());

        a.Realised.ShouldBe(new PaperRealisedView(29.00m, 40.00m, -11.00m, PaperWording.AfterStoredSheet));
        (a.Equity.State, a.Equity.Number).ShouldBe(("value", 99_989.00));
        a.FundsAvailable.Number.ShouldBe(97_989.00);
        Math.Round(a.OpenRiskPct.Number!.Value, 3).ShouldBe(0.100);
        a.Unrealised.ShouldBe(new XDrishti.Application.Reporting.MetricView("value", 40.00, null));
        a.LastSessionResult.ShouldBe(-120.00m);
        a.MaxDrawdownClosed.Amount.ShouldBe(120.00m);
        Math.Round(a.MaxDrawdownClosed.Pct.Number!.Value, 4).ShouldBe(0.1199);
        a.OpenCount.ShouldBe(1);
        a.ChargeSheet.ShouldBe(new PaperChargeSheetView("flat-10", null)); // never confirmed in this view
        a.Evidence.ShouldBe(new PaperEvidenceView(2, 1, PaperLedgerFixture.Day1, PaperLedgerFixture.Last));
        a.Limits.ShouldBe(PaperWording.Limits);
    }

    [Fact]
    public async Task A_stale_mark_makes_the_account_unrealised_unavailable_with_no_number()
    {
        var f = new PaperLedgerFixture();
        f.Closes["CCC"] = f.Closes["CCC"] with { Close = new DailyClose(new DateOnly(2026, 11, 4), 51.00m, true) };

        var a = await AccountAsync(f);
        var positions = await new GetPaperPositionsHandler(f.Context()).HandleAsync(new GetPaperPositionsQuery(PaperLedgerFixture.AccountId, null), Ct);

        a.Unrealised.State.ShouldBe("unavailable");
        a.Unrealised.Number.ShouldBeNull();
        var open = positions.Value.Open.ShouldHaveSingleItem();
        open.Mark.State.ShouldBe("unavailable");
        open.Mark.Price.ShouldBeNull();
        open.Mark.Reason.ShouldNotBeNullOrWhiteSpace();
        open.Unrealised.Number.ShouldBeNull();
        positions.Value.Closed.Select(c => c.Symbol).ShouldBe(["BBB", "AAA"]); // newest exit first
    }

    [Fact]
    public async Task An_account_without_capital_shows_the_result_but_equity_and_percentages_are_unavailable()
    {
        var f = new PaperLedgerFixture();
        f.Active = [PaperLedgerFixture.Make("1000000013", null)];
        f.Trades = [PaperLedgerFixture.B() with { AccountId = f.Active[0].Id }];

        var a = await AccountAsync(f);

        a.Realised.Net.ShouldBe(-120.00m);
        foreach (var m in new[] { a.Equity, a.FundsAvailable, a.OpenRiskPct, a.LastSessionPct, a.MaxDrawdownClosed.Pct })
        {
            m.State.ShouldBe("unavailable");
            m.Number.ShouldBeNull();
            m.Reason.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Before_the_first_capture_the_account_is_listed_with_nothing_traded()
    {
        var f = new PaperLedgerFixture();
        f.CurrentEpoch = null;

        var a = await AccountAsync(f);

        a.EpochId.ShouldBeNull();
        a.OpenCount.ShouldBe(0);
        a.Realised.Basis.ShouldBe(PaperWording.Gross);
        a.Unrealised.State.ShouldBe("empty");
    }

    [Fact]
    public async Task An_unknown_epoch_or_a_non_paper_account_is_not_found_and_a_bad_range_is_invalid()
    {
        var f = new PaperLedgerFixture();
        var accounts = new GetPaperAccountsHandler(f.Context(), NullLogger<GetPaperAccountsHandler>.Instance);
        var positions = new GetPaperPositionsHandler(f.Context());
        var equity = new GetPaperEquityHandler(f.Context(), f.Data);

        (await accounts.HandleAsync(new GetPaperAccountsQuery(99), Ct)).Error.Code.ShouldBe(PaperErrors.NotFoundCode);
        var unknown = await positions.HandleAsync(new GetPaperPositionsQuery(Guid.NewGuid(), null), Ct);
        unknown.Error.Code.ShouldBe(PaperErrors.NotFoundCode);
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
        var bad = await equity.HandleAsync(new GetPaperEquityQuery(PaperLedgerFixture.AccountId, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 3), null), Ct);
        bad.Error.Code.ShouldBe(PaperErrors.InvalidCode);
    }

    [Fact]
    public async Task The_equity_line_has_one_point_per_session_from_the_epoch_start()
    {
        var f = new PaperLedgerFixture();

        var view = (await new GetPaperEquityHandler(f.Context(), f.Data).HandleAsync(new GetPaperEquityQuery(PaperLedgerFixture.AccountId, null, null, null), Ct)).Value;

        view.Points.Select(p => (p.Date.Day, p.Realised, p.Equity.Number)).ShouldBe([(3, 109.00m, 100_109.00), (4, 109.00m, 100_109.00), (5, -11.00m, 99_989.00)]);
    }

    [Fact]
    public async Task Reconcile_is_ok_when_the_stored_sums_equal_the_ledger()
    {
        var f = new PaperLedgerFixture();

        var view = (await new ReconcilePaperHandler(f.Context(), f.Data, NullLogger<ReconcilePaperHandler>.Instance).HandleAsync(new ReconcilePaperQuery(PaperLedgerFixture.AccountId, null), Ct)).Value;

        view.Status.ShouldBe("ok");
        view.Differences.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconcile_lists_each_difference()
    {
        var f = new PaperLedgerFixture();
        var stranger = Guid.NewGuid();
        f.Totals = new StoredTotals(-10.00m, 2, 1);
        f.EntryAccounts = [PaperLedgerFixture.AccountId, stranger];

        var view = (await new ReconcilePaperHandler(f.Context(), f.Data, NullLogger<ReconcilePaperHandler>.Instance).HandleAsync(new ReconcilePaperQuery(PaperLedgerFixture.AccountId, null), Ct)).Value;

        view.Status.ShouldBe("differences");
        view.Differences.Count.ShouldBe(4);
        view.Differences.ShouldContain(d => d.Contains("-10.00", StringComparison.Ordinal) && d.Contains("-11.00", StringComparison.Ordinal));
        view.Differences.ShouldContain(d => d.Contains(stranger.ToString(), StringComparison.Ordinal) && d.Contains("not a paper account", StringComparison.Ordinal));
    }
}
