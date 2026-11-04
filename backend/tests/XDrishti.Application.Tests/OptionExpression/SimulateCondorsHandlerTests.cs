using NSubstitute;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>
/// P14-10 criteria 3 and 4 (use case): the condor verb is refused before any bar, quality verdict or calendar is read when no regime is stored, when the underlying is not an index and when the spread model is short of 60 sessions; with both gates it enters the first session of each week after a range
/// session, stores one row per entry with four legs per paired one under a population key, and writes nothing the second time.
/// </summary>
public sealed class SimulateCondorsHandlerTests
{
    private static readonly DateOnly From = new(2025, 6, 2);
    private static readonly DateOnly To = new(2025, 6, 13);
    private readonly StructureRig _rig = new();

    private SimulateCondorsCommand Command(string? profileHash = null, string underlying = "NSE:NIFTY") => new(profileHash ?? Register(), From, To, underlying);

    private string Register()
    {
        var profile = CondorParams.Primary.ToProfile().Value;
        _rig.Register(profile);
        return profile.Hash;
    }

    private async Task NothingWasReadAsync()
    {
        await _rig.Bars.DidNotReceiveWithAnyArgs().BarsAsync(default, default, default, default!, default, default, default, TestContext.Current.CancellationToken);
        await _rig.Quality.DidNotReceiveWithAnyArgs().RollingResultsAsync(default, default, default, default, TestContext.Current.CancellationToken);
        await _rig.Calendar.DidNotReceiveWithAnyArgs().GetAsync(TestContext.Current.CancellationToken);
        _rig.Store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_stored_regime_the_condor_is_refused_as_regime_unavailable_with_no_bar_quality_or_calendar_read()
    {
        _rig.Model(60);

        var result = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(RemarkStructureHandler.RegimeUnavailableCode);
        await NothingWasReadAsync();
    }

    [Fact]
    public async Task A_model_of_59_sessions_none_at_all_or_only_an_owner_assumption_is_refused_as_spread_not_measured_with_nothing_read()
    {
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.SpreadSettings.AssumedHalfSpreadPct(Arg.Any<string>()).Returns(1m);

        var none = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);
        _rig.Model(59);
        var short59 = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);

        none.Error.Code.ShouldBe(RemarkStructureHandler.SpreadNotMeasuredCode);
        short59.Error.Code.ShouldBe(RemarkStructureHandler.SpreadNotMeasuredCode);
        await NothingWasReadAsync();
    }

    [Fact]
    public async Task A_share_a_bad_date_range_an_unknown_profile_and_a_profile_of_another_kind_are_named_refusals()
    {
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var debit = StructureKit().ToProfile().Value;
        _rig.Register(debit);

        var share = await _rig.Condors().HandleAsync(Command(underlying: "NSE:RELIANCE"), TestContext.Current.CancellationToken);
        var dates = await _rig.Condors().HandleAsync(Command() with { From = To, To = From }, TestContext.Current.CancellationToken);
        var unknown = await _rig.Condors().HandleAsync(Command(new string('9', 64)), TestContext.Current.CancellationToken);
        var other = await _rig.Condors().HandleAsync(Command(debit.Hash), TestContext.Current.CancellationToken);

        share.Error.Code.ShouldBe(SimulateCondorsHandler.IndexOnlyCode);
        dates.Error.Code.ShouldBe(SimulateCondorsHandler.InvalidCode);
        unknown.Error.Code.ShouldBe(RemarkRunHandler.ProfileNotFoundCode);
        other.Error.Code.ShouldBe(CondorParams.InvalidCode);
        await NothingWasReadAsync();
    }

    [Fact]
    public async Task With_a_regime_and_a_measured_model_each_week_after_a_range_session_is_one_entry_with_four_legs_under_a_population_key()
    {
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.RangeOn(new DateOnly(2025, 5, 30), new DateOnly(2025, 6, 6)); // the sessions before Monday 06-02 and Monday 06-09
        _rig.Model(60);

        var result = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Trades.ShouldBe(2);
        result.Value.Paired.ShouldBe(2, string.Join("; ", result.Value.Unpaired.Where(u => u.Value > 0).Select(u => u.Key + " " + u.Value)));
        _rig.Store.Run!.CashRunKey.ShouldBe(SimulateCondorsHandler.Population(Command()));
        _rig.Store.Run.InputsJson.ShouldContain("\"from\":\"2025-06-02\"");
        _rig.Store.Rows.ShouldAllBe(r => r.OptionSide == "IC" && r.CashR == null && r.Labels.Contains(StructureNames.NoCashTwin) && r.Units == 75d);
        _rig.Store.Rows.Select(r => r.ExitReason).ShouldAllBe(reason => reason == RemarkFates.ExpiryExit);
        _rig.Store.Legs.Count.ShouldBe(8);
        _rig.Store.Legs.Select(l => (l.Leg, l.Position)).Distinct().Count().ShouldBe(4);
        result.Value.Lines.ShouldContain(l => l.Contains("2 entries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_archive_with_no_bar_of_the_underlying_is_refused_and_nothing_is_stored()
    {
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.Model(60);
        _rig.Bars.HasSeriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(SimulateCondorsHandler.EmptyArchiveCode);
        await NothingWasReadAsync();
    }

    [Fact]
    public async Task A_week_after_a_session_that_is_not_range_is_not_entered_and_the_second_run_writes_nothing()
    {
        _rig.Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _rig.RangeOn(new DateOnly(2025, 6, 6));
        _rig.Model(60);

        var first = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var second = await _rig.Condors().HandleAsync(Command(), TestContext.Current.CancellationToken);

        first.Value.Trades.ShouldBe(1);
        first.Value.Lines.ShouldContain(l => l.Contains("1 not after a range session", StringComparison.Ordinal));
        second.Value.Stored.ShouldBeFalse();
        second.Value.RunId.ShouldBe(first.Value.RunId);
        _rig.Store.Appends.ShouldBe(1);
    }

    private static SpreadParams StructureKit() => SpreadParams.Create(XDrishti.Domain.Learning.Expression.DebitSpread, "atm", 2, "nearest_safe", null, 1).Value;
}
