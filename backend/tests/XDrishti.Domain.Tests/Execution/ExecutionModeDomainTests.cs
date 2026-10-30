using XDrishti.Domain.Accounts;
using XDrishti.Domain.Execution;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>P12-07 criteria 1, 2, 3, 9 and 10 (cooling-off) at the pure level, hand tables with the boundaries.</summary>
public sealed class ExecutionModeDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 6, 0, 0, TimeSpan.Zero);

    private static ModeGateFacts AllGood(DateTimeOffset? suite = null, DateTimeOffset? ip = null) =>
        new(true, true, true, "MET", suite ?? Now.AddDays(-3), ip ?? Now.AddDays(-3));

    [Theory]
    [InlineData("paper", LadderMode.Paper)]
    [InlineData("manual_live", LadderMode.ManualLive)]
    [InlineData("assisted", LadderMode.Assisted)]
    public void A_rung_has_one_name_both_ways(string name, LadderMode mode)
    {
        ExecutionModes.TryParse(name).ShouldBe(mode);
        ExecutionModes.Name(mode).ShouldBe(name);
    }

    [Theory]
    [InlineData("semi_auto")]
    [InlineData("auto_live")]
    [InlineData("Assisted")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_rung_is_not_a_mode_and_the_automated_modes_do_not_exist_yet(string? name) => ExecutionModes.TryParse(name).ShouldBeNull();

    [Fact]
    public void A_promotion_is_one_step_up_and_a_demotion_may_skip_steps()
    {
        ExecutionModes.IsAdjacentPromotion(LadderMode.ManualLive, LadderMode.Assisted).ShouldBeTrue();
        ExecutionModes.IsAdjacentPromotion(LadderMode.Paper, LadderMode.Assisted).ShouldBeFalse();
        ExecutionModes.IsAdjacentPromotion(LadderMode.Assisted, LadderMode.ManualLive).ShouldBeFalse();
        ExecutionModes.IsAdjacentPromotion(LadderMode.ManualLive, LadderMode.ManualLive).ShouldBeFalse();
        ExecutionModes.IsDemotion(LadderMode.Assisted, LadderMode.Paper).ShouldBeTrue();
        ExecutionModes.IsDemotion(LadderMode.Paper, LadderMode.Paper).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, TradingMode.Paper, LadderMode.Paper)]
    [InlineData(null, TradingMode.ManualLive, LadderMode.ManualLive)]
    [InlineData("assisted", TradingMode.ManualLive, LadderMode.Assisted)]
    [InlineData("assisted", TradingMode.Paper, LadderMode.Paper)] // the configuration is a ceiling
    [InlineData("manual_live", TradingMode.Paper, LadderMode.Paper)]
    [InlineData("manual_live", TradingMode.ManualLive, LadderMode.ManualLive)]
    [InlineData("paper", TradingMode.ManualLive, LadderMode.Paper)]
    [InlineData("semi_auto", TradingMode.ManualLive, LadderMode.Paper)] // not a rung: the safer side
    [InlineData("garbage", TradingMode.ManualLive, LadderMode.Paper)]
    public void The_effective_mode_is_the_trail_capped_by_the_configuration_and_an_unknown_name_reads_paper(string? trail, TradingMode configured, LadderMode expected) =>
        ExecutionModes.Effective(trail, ExecutionModes.Configured(configured)).ShouldBe(expected);

    [Fact]
    public void With_every_fact_good_the_step_to_assisted_is_eligible_and_lists_every_check()
    {
        var gate = ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(), Now);

        gate.Eligible.ShouldBeTrue();
        gate.Checks.Select(c => c.Code).ShouldBe(["execution_enabled", "trading_role", "connection", "live_phase_check", "sandbox_suite", "static_ip_attestation"]);
        gate.Reasons.ShouldBeEmpty();
        gate.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Every_fact_that_fails_or_is_unavailable_closes_the_gate_and_every_reason_is_listed()
    {
        var gate = ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, new ModeGateFacts(false, null, false, "NOT MET", null, Now.AddDays(-40)), Now);

        gate.Eligible.ShouldBeFalse();
        gate.Reasons.Count.ShouldBe(6);
        gate.Checks.Select(c => c.OutcomeName).ShouldBe(["fail", "unavailable", "fail", "fail", "unavailable", "fail"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void Each_single_bad_fact_alone_closes_the_gate(bool? bad)
    {
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { ExecutionEnabled = bad }, Now).Eligible.ShouldBeFalse();
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { TradingRoleOn = bad }, Now).Eligible.ShouldBeFalse();
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { ConnectionConnected = bad }, Now).Eligible.ShouldBeFalse();
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { LivePhaseVerdict = bad is null ? null : "NOT EVALUABLE" }, Now).Eligible.ShouldBeFalse();
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void A_record_counts_for_at_most_thirty_days_and_a_missing_one_never_counts(int days, bool eligible)
    {
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(suite: Now.AddDays(-days)), Now).Eligible.ShouldBe(eligible);
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(ip: Now.AddDays(-days)), Now).Eligible.ShouldBe(eligible);
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { SandboxSuitePassedAt = null }, Now).Eligible.ShouldBeFalse();
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { StaticIpAttestedAt = null }, Now).Eligible.ShouldBeFalse();
        ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(suite: Now.AddDays(1)), Now).Eligible.ShouldBeFalse(); // a record dated in the future is not evidence
    }

    [Theory]
    [InlineData(LadderMode.Paper, LadderMode.Assisted, "not_adjacent")]
    [InlineData(LadderMode.Assisted, LadderMode.Assisted, "not_adjacent")]
    [InlineData(LadderMode.Assisted, LadderMode.ManualLive, "not_adjacent")]
    [InlineData(LadderMode.Paper, LadderMode.ManualLive, "not_gated_here")]
    public void Every_other_pair_is_one_failed_check_that_says_why(LadderMode from, LadderMode to, string code)
    {
        var gate = ModeGate.Evaluate(from, to, AllGood(), Now);

        gate.Eligible.ShouldBeFalse();
        gate.Checks.Select(c => c.Code).ShouldBe([code]);
    }

    [Fact]
    public void The_hash_follows_the_evidence_and_not_the_clock()
    {
        var a = ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(), Now);
        var b = ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood(), Now);
        var c = ModeGate.Evaluate(LadderMode.ManualLive, LadderMode.Assisted, AllGood() with { LivePhaseVerdict = "NOT MET" }, Now);

        b.Hash.ShouldBe(a.Hash);
        c.Hash.ShouldNotBe(a.Hash);
    }

    [Theory]
    [InlineData(LadderMode.Paper, true, false, null)]
    [InlineData(LadderMode.ManualLive, true, false, null)]
    [InlineData(LadderMode.Assisted, true, false, null)]
    [InlineData(LadderMode.Assisted, false, false, "role_off")]
    [InlineData(LadderMode.ManualLive, false, false, "role_off")]
    [InlineData(LadderMode.Assisted, true, true, "archived")]
    [InlineData(LadderMode.Assisted, null, false, "role_unreadable")]
    [InlineData(LadderMode.Assisted, true, null, "archived_unreadable")]
    [InlineData(LadderMode.Paper, false, true, null)] // nothing lower than paper
    public void The_system_demotes_to_paper_when_the_role_is_off_or_the_account_archived_and_an_unreadable_fact_counts_as_the_bad_case(LadderMode current, bool? role, bool? archived, string? code)
    {
        var decision = DemotionPolicy.Evaluate(current, new DemotionFacts(role, archived));

        decision?.Code.ShouldBe(code);
        (decision is null).ShouldBe(code is null);
        decision?.Target.ShouldBe(LadderMode.Paper);
    }

    [Fact]
    public void The_cooling_off_runs_from_the_close_of_the_second_session_to_the_close_of_the_fifth_and_skips_weekends_and_holidays()
    {
        var calendar = new TradingCalendar();
        var monday = new DateTimeOffset(2026, 10, 26, 5, 0, 0, TimeSpan.Zero); // Monday 10:30 IST

        var window = PromotionTiming.Window(monday, calendar);

        window.EligibleFrom.ShouldBe(new DateTimeOffset(2026, 10, 28, 15, 30, 0, TimeSpan.FromMinutes(330))); // Wednesday close: the 2nd session after Monday
        window.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 11, 2, 15, 30, 0, TimeSpan.FromMinutes(330))); // Monday close: the 5th (Tue, Wed, Thu, Fri, Mon)
        PromotionTiming.Window(new DateTimeOffset(2026, 10, 30, 5, 0, 0, TimeSpan.Zero), calendar).EligibleFrom.ShouldBe(new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.FromMinutes(330))); // Friday: Monday, Tuesday
    }

    [Fact]
    public void A_promotion_takes_effect_at_nine_india_time_of_the_next_trading_day()
    {
        var calendar = new TradingCalendar();

        PromotionTiming.EffectiveFrom(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero), calendar).ShouldBe(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromMinutes(330)));
        PromotionTiming.EffectiveFrom(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero), calendar).ShouldBe(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromMinutes(330))); // Friday to Monday
    }
}
