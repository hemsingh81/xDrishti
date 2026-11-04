using NSubstitute;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>
/// P14-10 criteria 3, 5 and 7 (use case) on synthetic data: a debit spread run gives one row per trade and two legs per paired one and writes nothing the second time; a credit spread without a stored regime is refused before any bar is read, with a model of 59 sessions every trade is
/// <c>spread_unavailable</c> with no bar read, and with both it trades only after a range session; a stock width of 4 is refused before any bar is read; one verb reaches the right use case by the kind of the profile.
/// </summary>
public sealed class RemarkStructureHandlerTests
{
    private readonly StructureRig _rig = new();

    private static SpreadParams Debit(int width = 2) => SpreadParams.Create(Expression.DebitSpread, "atm", width, "nearest_safe", null, 1).Value;

    private static SpreadParams Credit() => SpreadParams.Create(Expression.CreditSpread, "atm", 2, "nearest_safe", null, 1).Value;

    private async Task<XDrishti.Domain.Common.Result<RemarkRunSummary>> RunAsync(SpreadParams profile)
    {
        var stored = profile.ToProfile().Value;
        _rig.Register(stored);
        return await _rig.Dispatcher().HandleAsync(new RemarkRunCommand(StructureRig.RunKey, stored.Hash), TestContext.Current.CancellationToken);
    }

    private async Task NoBarWasReadAsync()
    {
        await _rig.Bars.DidNotReceiveWithAnyArgs().BarsAsync(default, default, default, default!, default, default, default, TestContext.Current.CancellationToken);
        await _rig.Quality.DidNotReceiveWithAnyArgs().RollingResultsAsync(default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_debit_spread_run_gives_one_row_per_trade_two_legs_per_paired_trade_and_the_counts_in_the_run_row()
    {
        _rig.Run(StructureRig.Week());

        var result = await RunAsync(Debit());

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Trades.ShouldBe(5);
        result.Value.Paired.ShouldBe(5);
        _rig.Store.Rows.Select(r => r.TradeKey).Distinct().Count().ShouldBe(5);
        _rig.Store.Legs.Count.ShouldBe(10);
        _rig.Store.Legs.Select(l => l.TradeKey).Distinct().Count().ShouldBe(5);
        _rig.Store.Rows.Where(r => r.IsPaired).ShouldAllBe(r => r.Labels.Any(l => l.StartsWith("max_loss_px: ", StringComparison.Ordinal)) && r.Labels.Contains("structure: debit_spread"));
        _rig.Store.Rows.Count(r => r.OptionSide == "CE").ShouldBe(3); // a buy is a bull call spread, a sell a bear put spread
        _rig.Store.Rows.Count(r => r.OptionSide == "PE").ShouldBe(2);
        _rig.Store.Run!.Labels.ShouldContain("structure: debit_spread");
        _rig.Store.Run.UnpairedJson.ShouldContain("\"not_range\":0");
        _rig.Store.Run.UnpairedJson.ShouldContain("\"structure_invalid\":0");
    }

    [Fact]
    public async Task A_second_run_with_the_same_inputs_writes_nothing()
    {
        _rig.Run(StructureRig.Week());

        var first = await RunAsync(Debit());
        var second = await RunAsync(Debit());

        first.Value.Stored.ShouldBeTrue();
        second.Value.Stored.ShouldBeFalse();
        second.Value.RunId.ShouldBe(first.Value.RunId);
        _rig.Store.Appends.ShouldBe(1);
    }

    [Fact]
    public async Task A_credit_spread_without_a_stored_regime_is_refused_before_any_bar_is_read()
    {
        _rig.Run(StructureRig.Week());
        _rig.Model(60);

        var result = await RunAsync(Credit());

        result.Error.Code.ShouldBe(RemarkStructureHandler.RegimeUnavailableCode);
        await NoBarWasReadAsync();
        _rig.Store.Appends.ShouldBe(0);
        await _rig.Runs.DidNotReceiveWithAnyArgs().StreamTradesAsync(default!, default, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_credit_spread_on_a_model_of_59_sessions_or_only_an_assumption_is_refused_as_spread_not_measured_and_no_bar_is_read()
    {
        _rig.Run(StructureRig.Week());
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.RangeOn(StructureRig.Monday.AddDays(-3), StructureRig.Monday);
        _rig.Model(59);
        _rig.SpreadSettings.AssumedHalfSpreadPct(Arg.Any<string>()).Returns(1m);

        var result = await RunAsync(Credit());

        result.Error.Code.ShouldBe(RemarkStructureHandler.SpreadNotMeasuredCode);
        await NoBarWasReadAsync();
        await _rig.Bars.DidNotReceiveWithAnyArgs().HasSeriesAsync(default, TestContext.Current.CancellationToken);
        _rig.Store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_credit_spread_trades_only_after_a_range_session_and_counts_every_other_trade_as_not_range()
    {
        _rig.Run(StructureRig.Week());
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.RangeOn(new DateOnly(2025, 5, 30), new DateOnly(2025, 6, 3)); // the sessions before Monday 06-02 and Wednesday 06-04
        _rig.Model(60);

        var result = await RunAsync(Credit());

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Unpaired[StructureNames.NotRange].ShouldBe(3);
        result.Value.Paired.ShouldBe(2);
        _rig.Store.Rows.Where(r => r.Fate == StructureNames.NotRange).ShouldAllBe(r => r.REquiv == null && r.EntryPremiumPx == null);
        _rig.Store.Rows.Where(r => r.IsPaired).ShouldAllBe(r => r.Labels.Contains("structure: credit_spread"));
        _rig.Store.Legs.Count.ShouldBe(4);
        _rig.Store.Rows.Count(r => r.OptionSide == "PE").ShouldBe(3, "a buy is a bull put spread: all of its rows, paired or not, carry the put side");
        _rig.Store.Rows.Count(r => r.OptionSide == "CE").ShouldBe(2);
    }

    [Fact]
    public async Task An_archive_with_no_bar_of_any_underlying_of_the_run_is_refused_and_nothing_is_stored()
    {
        _rig.Run(StructureRig.Week());
        _rig.Bars.HasSeriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await RunAsync(Debit());

        result.Error.Code.ShouldBe(RemarkStructureHandler.EmptyArchiveCode);
        await NoBarWasReadAsync();
        _rig.Store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_stock_debit_spread_with_a_width_of_4_is_refused_at_validation_before_any_bar_is_read()
    {
        var shares = Enumerable.Range(0, 3).Select(i => StructureRig.Cash(StructureRig.Share, "NSE:RELIANCE", StructureRig.Monday.AddDays(i))).ToList();
        _rig.Run(shares);

        var result = await RunAsync(Debit(width: 4));

        result.Error.Code.ShouldBe(SpreadParams.WidthOutsideWindowCode);
        await NoBarWasReadAsync();
        _rig.Store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_stock_credit_spread_is_refused_as_index_only_and_a_condor_profile_has_no_cash_run_to_re_mark()
    {
        _rig.Run([StructureRig.Cash(StructureRig.Share, "NSE:RELIANCE", StructureRig.Monday)]);
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var condor = CondorParams.Primary.ToProfile().Value;
        _rig.Register(condor);

        var credit = await RunAsync(Credit());
        var routed = await _rig.Dispatcher().HandleAsync(new RemarkRunCommand(StructureRig.RunKey, condor.Hash), TestContext.Current.CancellationToken);

        credit.Error.Code.ShouldBe(SpreadParams.IndexOnlyCode);
        routed.Error.Code.ShouldBe(RemarkDispatchHandler.CondorHasNoCashRunCode);
        await NoBarWasReadAsync();
    }

    [Fact]
    public async Task An_unknown_profile_or_run_is_a_named_failure()
    {
        var noProfile = await _rig.Dispatcher().HandleAsync(new RemarkRunCommand(StructureRig.RunKey, new string('9', 64)), TestContext.Current.CancellationToken);
        _rig.Runs.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((XDrishti.Application.Abstractions.Research.StoredRunHeader?)null);
        var debit = Debit().ToProfile().Value;
        _rig.Register(debit);
        var noRun = await _rig.Structures().HandleAsync(new RemarkRunCommand(new string('8', 64), debit.Hash), new XDrishti.Application.Abstractions.Learning.StoredExpressionProfile(debit, DateTimeOffset.UnixEpoch), TestContext.Current.CancellationToken);

        noProfile.Error.Code.ShouldBe(RemarkRunHandler.ProfileNotFoundCode);
        noRun.Error.Code.ShouldBe(RemarkRunHandler.RunNotFoundCode);
        _rig.Store.Appends.ShouldBe(0);
    }
}
