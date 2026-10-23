using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Checklist;

public sealed class PreTradeChecklistTests
{
    private static readonly ChecklistLimits Limits = new();
    private static readonly ChecklistPolicy Policy = new();

    private static ChecklistTicket Ticket(string side = "BUY", double? price = 100, double? qty = 10, double? risk = 0.5, string? sector = "IT") =>
        new("AAA", side, price, qty, risk, sector);

    private static ChecklistAccount Account(
        TradingMode mode = TradingMode.Paper,
        bool trading = true,
        double? perTrade = 1.0,
        double? total = 3.0,
        int? perSector = 2,
        double? lossLimit = 2.0,
        double? funds = 10000,
        double? openRisk = 1.0,
        int? sectorPositions = 0,
        double? loss = 0) =>
        new("acc-1", mode, trading, perTrade, total, perSector, lossLimit, funds, openRisk, sectorPositions, loss);

    private static ChecklistFacts Facts() =>
        new(true, false, false, 10, false, false, 90, 110, 10000, 0.3);

    private static ChecklistResult Run(ChecklistTicket? t = null, ChecklistAccount? a = null, ChecklistFacts? f = null) =>
        PreTradeChecklist.Evaluate(t ?? Ticket(), a ?? Account(), f ?? Facts(), Limits, Policy);

    private static ChecklistCheck Of(ChecklistResult r, string code) => r.Checks.Single(c => c.Code == code);

    [Fact]
    public void A_clean_ticket_passes_every_check_and_can_be_approved()
    {
        var r = Run();

        r.Checks.ShouldAllBe(c => c.Outcome == FilterOutcome.Pass);
        r.Checks.Count.ShouldBe(14);
        r.Review().CanApprove.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1.0, FilterOutcome.Pass)]
    [InlineData(1.01, FilterOutcome.Fail)]
    public void Risk_profile_at_the_limit_passes_and_just_beyond_fails(double risk, FilterOutcome expected)
    {
        var c = Of(Run(Ticket(risk: risk)), ChecklistCodes.RiskProfile);

        c.Outcome.ShouldBe(expected);
        c.Value.ShouldBe(risk);
        c.Limit.ShouldBe(1.0);
    }

    [Fact]
    public void Risk_profile_without_a_limit_or_a_risk_is_unavailable()
    {
        Of(Run(a: Account(perTrade: null)), ChecklistCodes.RiskProfile).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(risk: null)), ChecklistCodes.RiskProfile).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(risk: double.NaN)), ChecklistCodes.RiskProfile).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(1000, FilterOutcome.Pass)]
    [InlineData(999.99, FilterOutcome.Fail)]
    public void Capital_headroom_at_the_cost_passes_and_just_below_fails(double funds, FilterOutcome expected)
    {
        var c = Of(Run(a: Account(funds: funds)), ChecklistCodes.CapitalHeadroom);

        c.Outcome.ShouldBe(expected);
        c.Value.ShouldBe(1000);
    }

    [Fact]
    public void Capital_headroom_without_funds_is_unavailable()
    {
        Of(Run(a: Account(funds: null)), ChecklistCodes.CapitalHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(2.5, FilterOutcome.Pass)]
    [InlineData(2.51, FilterOutcome.Fail)]
    public void Exposure_headroom_adds_the_ticket_to_the_open_risk(double open, FilterOutcome expected)
    {
        var c = Of(Run(a: Account(openRisk: open)), ChecklistCodes.ExposureHeadroom);

        c.Outcome.ShouldBe(expected);
        c.Limit.ShouldBe(3.0);
    }

    [Fact]
    public void Exposure_headroom_without_the_open_risk_is_unavailable()
    {
        Of(Run(a: Account(openRisk: null)), ChecklistCodes.ExposureHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(1.99, FilterOutcome.Pass)]
    [InlineData(2.0, FilterOutcome.Fail)]
    [InlineData(3.0, FilterOutcome.Fail)]
    public void Daily_loss_reaching_the_limit_fails(double loss, FilterOutcome expected)
    {
        Of(Run(a: Account(loss: loss)), ChecklistCodes.DailyLoss).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Daily_loss_unknown_is_unavailable()
    {
        Of(Run(a: Account(loss: null)), ChecklistCodes.DailyLoss).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(lossLimit: null)), ChecklistCodes.DailyLoss).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(true, FilterOutcome.Pass)]
    [InlineData(false, FilterOutcome.Fail)]
    [InlineData(null, FilterOutcome.Unavailable)]
    public void Data_quality_follows_the_validation_status(bool? ok, FilterOutcome expected)
    {
        Of(Run(f: Facts() with { DataQualityOk = ok }), ChecklistCodes.DataQuality).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData("BUY", false, false, FilterOutcome.Pass)]
    [InlineData("BUY", true, null, FilterOutcome.Fail)]
    [InlineData("SELL", false, true, FilterOutcome.Fail)]
    [InlineData("BUY", false, true, FilterOutcome.Pass)]
    public void Holdings_conflict_is_a_sell_against_a_long_holding_or_an_open_trade(string side, bool openTrade, bool? heldLong, FilterOutcome expected)
    {
        var f = Facts() with { OpenTradeExists = openTrade, HeldLongAnyAccount = heldLong };

        Of(Run(Ticket(side), f: f), ChecklistCodes.HoldingsConflict).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Holdings_conflict_with_missing_input_is_unavailable_but_a_known_conflict_still_fails()
    {
        Of(Run(Ticket("SELL"), f: Facts() with { HeldLongAnyAccount = null }), ChecklistCodes.HoldingsConflict).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { OpenTradeExists = null }), ChecklistCodes.HoldingsConflict).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket("SELL"), f: Facts() with { HeldLongAnyAccount = true, OpenTradeExists = null }), ChecklistCodes.HoldingsConflict).Outcome.ShouldBe(FilterOutcome.Fail);
        Of(Run(Ticket("HOLD")), ChecklistCodes.HoldingsConflict).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(1, FilterOutcome.Pass)]
    [InlineData(2, FilterOutcome.Fail)]
    public void Sector_exposure_counts_the_ticket_itself(int existing, FilterOutcome expected)
    {
        var c = Of(Run(a: Account(sectorPositions: existing)), ChecklistCodes.SectorExposure);

        c.Outcome.ShouldBe(expected);
        c.Value.ShouldBe(existing + 1);
    }

    [Fact]
    public void Sector_exposure_without_a_sector_or_count_is_unavailable()
    {
        Of(Run(Ticket(sector: null)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(sectorPositions: null)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(perSector: null)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(4, FilterOutcome.Pass)]
    [InlineData(3, FilterOutcome.Fail)]
    [InlineData(0, FilterOutcome.Fail)]
    public void Results_within_three_days_fail(int days, FilterOutcome expected)
    {
        Of(Run(f: Facts() with { DaysToResults = days }), ChecklistCodes.ResultsWithinDays).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Results_date_missing_or_invalid_is_unavailable_and_never_a_pass(int? days)
    {
        Of(Run(f: Facts() with { DaysToResults = days }), ChecklistCodes.ResultsWithinDays).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(false, FilterOutcome.Pass)]
    [InlineData(true, FilterOutcome.Fail)]
    [InlineData(null, FilterOutcome.Unavailable)]
    public void Fno_ban_follows_the_list_and_missing_list_is_unavailable(bool? banned, FilterOutcome expected)
    {
        Of(Run(f: Facts() with { InFnoBan = banned }), ChecklistCodes.FnoBan).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, FilterOutcome.Pass)]
    [InlineData(true, FilterOutcome.Fail)]
    [InlineData(null, FilterOutcome.Unavailable)]
    public void Surveillance_follows_the_status(bool? under, FilterOutcome expected)
    {
        Of(Run(f: Facts() with { UnderSurveillance = under }), ChecklistCodes.Surveillance).Outcome.ShouldBe(expected);
    }

    [Theory]
    [InlineData(90.01, FilterOutcome.Pass)]
    [InlineData(90.0, FilterOutcome.Fail)]
    [InlineData(109.99, FilterOutcome.Pass)]
    [InlineData(110.0, FilterOutcome.Fail)]
    [InlineData(120.0, FilterOutcome.Fail)]
    public void Price_band_edges_are_locked_circuits_and_fail(double price, FilterOutcome expected)
    {
        Of(Run(Ticket(price: price), f: Facts() with { PriceBandLower = 90, PriceBandUpper = 110 }), ChecklistCodes.PriceBand).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void Price_band_missing_or_inverted_is_unavailable()
    {
        Of(Run(f: Facts() with { PriceBandUpper = null }), ChecklistCodes.PriceBand).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { PriceBandLower = 120, PriceBandUpper = 110 }), ChecklistCodes.PriceBand).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(1000, FilterOutcome.Pass)]
    [InlineData(999, FilterOutcome.Fail)]
    public void Liquidity_is_a_share_of_the_average_daily_volume(double adv, FilterOutcome expected)
    {
        var c = Of(Run(Ticket(qty: 10), f: Facts() with { AverageDailyVolume = adv }), ChecklistCodes.Liquidity);

        c.Outcome.ShouldBe(expected);
        c.Limit.ShouldBe(1.0);
    }

    [Fact]
    public void Liquidity_without_volume_is_unavailable()
    {
        Of(Run(f: Facts() with { AverageDailyVolume = null }), ChecklistCodes.Liquidity).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(0.7, FilterOutcome.Pass)]
    [InlineData(0.71, FilterOutcome.Fail)]
    [InlineData(null, FilterOutcome.Unavailable)]
    public void Correlation_at_the_limit_passes_and_unknown_is_unavailable(double? corr, FilterOutcome expected)
    {
        Of(Run(f: Facts() with { MaxCorrelation = corr }), ChecklistCodes.Correlation).Outcome.ShouldBe(expected);
    }

    [Fact]
    public void An_account_in_manual_live_mode_is_refused_with_one_check_and_nothing_else()
    {
        var r = Run(a: Account(mode: TradingMode.ManualLive));

        r.Refused.ShouldBeTrue();
        r.Checks.Count.ShouldBe(1);
        r.Checks[0].Code.ShouldBe(ChecklistCodes.AccountPaper);
        r.Review().CanApprove.ShouldBeFalse();
    }

    [Fact]
    public void An_account_without_the_trading_role_is_refused()
    {
        Run(a: Account(trading: false)).Refused.ShouldBeTrue();
    }

    [Fact]
    public void A_confirmation_never_overrides_a_refused_account()
    {
        var confirm = OwnerConfirmation.Create("owner:hem", "I know").Value;

        var review = Run(a: Account(mode: TradingMode.ManualLive)).Review(confirm);

        review.CanApprove.ShouldBeFalse();
        review.Checks.Single().Standing.ShouldBe(CheckStanding.Refused);
        review.Confirmation.ShouldBeNull();
    }

    [Fact]
    public void An_unavailable_check_blocks_approval_until_the_owner_confirms()
    {
        var r = Run(f: Facts() with { InFnoBan = null, DaysToResults = null });

        var review = r.Review();

        review.CanApprove.ShouldBeFalse();
        review.Checks.Where(c => c.Standing == CheckStanding.NeedsConfirmation).Select(c => c.Check.Code)
            .ShouldBe([ChecklistCodes.ResultsWithinDays, ChecklistCodes.FnoBan]);
    }

    [Fact]
    public void A_warn_policy_fail_does_not_block_but_an_unavailable_one_with_warn_policy_still_does()
    {
        var failing = Run(Ticket("SELL"), f: Facts() with { HeldLongAnyAccount = true }).Review();
        var unknown = Run(Ticket("SELL"), f: Facts() with { HeldLongAnyAccount = null }).Review();

        failing.CanApprove.ShouldBeTrue();
        failing.Checks.Single(c => c.Check.Code == ChecklistCodes.HoldingsConflict).Standing.ShouldBe(CheckStanding.Warned);
        unknown.CanApprove.ShouldBeFalse();
    }

    [Fact]
    public void A_policy_override_can_make_a_warn_check_block()
    {
        var policy = new ChecklistPolicy(new Dictionary<string, CheckPolicy> { [ChecklistCodes.HoldingsConflict] = CheckPolicy.BlockWithConfirm });

        var r = PreTradeChecklist.Evaluate(Ticket("SELL"), Account(), Facts() with { HeldLongAnyAccount = true }, Limits, policy);

        r.Review().CanApprove.ShouldBeFalse();
    }

    [Fact]
    public void A_confirmation_allows_approval_but_shows_confirmed_despite_and_keeps_the_outcome()
    {
        var r = Run(f: Facts() with { InFnoBan = null });
        var confirm = OwnerConfirmation.Create("owner:hem", "  ban list not wired; checked NSE site  ").Value;

        var review = r.Review(confirm);

        review.CanApprove.ShouldBeTrue();
        review.ConfirmedDespiteCodes.ShouldBe([ChecklistCodes.FnoBan]);
        var ban = review.Checks.Single(c => c.Check.Code == ChecklistCodes.FnoBan);
        ban.Standing.ShouldBe(CheckStanding.ConfirmedDespite);
        ban.Check.Outcome.ShouldBe(FilterOutcome.Unavailable);
        review.Confirmation!.Reason.ShouldBe("ban list not wired; checked NSE site");
        review.Confirmation.Actor.ShouldBe("owner:hem");
    }

    [Fact]
    public void A_confirmation_that_nothing_needs_is_not_recorded()
    {
        var confirm = OwnerConfirmation.Create("owner:hem", "just in case").Value;

        var review = Run().Review(confirm);

        review.Confirmation.ShouldBeNull();
        review.ConfirmedDespiteCodes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, "reason")]
    [InlineData("", "reason")]
    [InlineData("system", "reason")]
    [InlineData("service:nightly", "reason")]
    [InlineData("Owner:hem", "reason")]
    [InlineData("owner:", "reason")]
    [InlineData("owner:   ", "reason")]
    public void Only_an_owner_actor_can_confirm(string? actor, string reason)
    {
        var made = OwnerConfirmation.Create(actor, reason);

        made.IsFailure.ShouldBeTrue();
        made.Error.Code.ShouldBe("checklist.owner_only");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_confirmation_needs_a_reason(string? reason)
    {
        var made = OwnerConfirmation.Create("owner:hem", reason);

        made.IsFailure.ShouldBeTrue();
        made.Error.Code.ShouldBe("checklist.reason_required");
    }

    [Fact]
    public void A_reason_over_the_limit_is_refused_and_one_at_the_limit_is_kept()
    {
        OwnerConfirmation.Create("owner:hem", new string('x', 501)).Error.Code.ShouldBe("checklist.reason_too_long");
        OwnerConfirmation.Create("owner:hem", new string('x', 500)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Same_inputs_give_the_same_checks_and_inputs_are_not_changed()
    {
        var t = Ticket();
        var a = Account();
        var f = Facts() with { InFnoBan = null };

        var one = PreTradeChecklist.Evaluate(t, a, f, Limits, Policy);
        var two = PreTradeChecklist.Evaluate(t, a, f, Limits, Policy);

        one.Checks.ShouldBe(two.Checks);
        t.ShouldBe(Ticket());
        a.ShouldBe(Account());
    }

    [Fact]
    public void Every_check_has_a_distinct_stable_code()
    {
        var codes = Run().Checks.Select(c => c.Code).ToList();

        codes.Distinct().Count().ShouldBe(codes.Count);
        codes[0].ShouldBe("account_paper");
    }
}
