using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Checklist;

/// <summary>Completeness of a review, the binding of the owner's confirmation, policy and limit validation, and bad numbers (spec P7-05 review fixes).</summary>
public sealed class ChecklistBindingTests
{
    private static ChecklistTicket Ticket(double? price = 100, double? qty = 10, double? risk = 0.5) => new("AAA", "BUY", price, qty, risk, "IT");

    private static ChecklistAccount Account(double? perTrade = 1.0, double? total = 3.0, int? perSector = 2, double? lossLimit = 2.0, double? funds = 10000, double? openRisk = 1.0, int? sectorPositions = 0, double? loss = 0) =>
        new("acc-1", TradingMode.Paper, true, perTrade, total, perSector, lossLimit, funds, openRisk, sectorPositions, loss);

    private static ChecklistFacts Facts() => new(true, false, false, 10, false, false, 90, 110, 10000, 0.3);

    private static ChecklistResult Run(ChecklistTicket? t = null, ChecklistAccount? a = null, ChecklistFacts? f = null) =>
        PreTradeChecklist.Evaluate(t ?? Ticket(), a ?? Account(), f ?? Facts(), ChecklistLimits.Default, ChecklistPolicy.Default);

    private static OwnerConfirmation Confirm(params string[] codes) => OwnerConfirmation.Create("owner:hem", "checked by hand", "acc-1", "AAA", codes).Value;

    private static ChecklistCheck Of(ChecklistResult r, string code) => r.Checks.Single(c => c.Code == code);

    private static ReviewedCheck Passed(string code) => new(new ChecklistCheck(code, FilterOutcome.Pass, CheckPolicy.BlockWithConfirm, null, null, "hand built"), CheckStanding.Passed);

    private static ChecklistReview Hand(IEnumerable<string> codes) => new("acc-1", "AAA", codes.Select(Passed).ToList(), null);

    [Fact]
    public void A_review_built_by_hand_is_approvable_only_when_complete_and_in_order()
    {
        Hand(ChecklistCodes.All).CanApprove.ShouldBeTrue();
        Hand([]).CanApprove.ShouldBeFalse();
        Hand(ChecklistCodes.All.Skip(1)).CanApprove.ShouldBeFalse();
        Hand(ChecklistCodes.All.Take(5)).CanApprove.ShouldBeFalse();
        Hand(ChecklistCodes.All.Append(ChecklistCodes.FnoBan)).CanApprove.ShouldBeFalse();
        Hand(ChecklistCodes.All.Skip(1).Append(ChecklistCodes.AccountPaper)).CanApprove.ShouldBeFalse();
    }

    [Fact]
    public void The_result_and_review_cannot_be_built_outside_the_domain_assembly()
    {
        typeof(ChecklistResult).GetConstructors().ShouldBeEmpty();
        typeof(ChecklistReview).GetConstructors().ShouldBeEmpty();
    }

    [Fact]
    public void A_confirmation_naming_one_code_leaves_a_second_unavailable_check_needing_confirmation()
    {
        var review = Run(f: Facts() with { InFnoBan = null, DaysToResults = null }).Review(Confirm(ChecklistCodes.FnoBan));

        review.CanApprove.ShouldBeFalse();
        review.Checks.Single(c => c.Check.Code == ChecklistCodes.FnoBan).Standing.ShouldBe(CheckStanding.ConfirmedDespite);
        review.Checks.Single(c => c.Check.Code == ChecklistCodes.ResultsWithinDays).Standing.ShouldBe(CheckStanding.NeedsConfirmation);
        review.ConfirmedDespiteCodes.ShouldBe([ChecklistCodes.FnoBan]);
    }

    [Fact]
    public void A_confirmation_naming_every_needing_code_allows_approval()
    {
        var review = Run(f: Facts() with { InFnoBan = null, DaysToResults = null }).Review(Confirm(ChecklistCodes.FnoBan, ChecklistCodes.ResultsWithinDays));

        review.CanApprove.ShouldBeTrue();
        review.ConfirmedDespiteCodes.ShouldBe([ChecklistCodes.ResultsWithinDays, ChecklistCodes.FnoBan]);
    }

    [Fact]
    public void A_confirmation_made_for_another_account_or_symbol_covers_nothing()
    {
        var r = Run(f: Facts() with { InFnoBan = null });
        var otherAccount = OwnerConfirmation.Create("owner:hem", "why", "acc-2", "AAA", [ChecklistCodes.FnoBan]).Value;
        var otherSymbol = OwnerConfirmation.Create("owner:hem", "why", "acc-1", "BBB", [ChecklistCodes.FnoBan]).Value;

        foreach (var wrong in new[] { otherAccount, otherSymbol })
        {
            var review = r.Review(wrong);

            review.CanApprove.ShouldBeFalse();
            review.Confirmation.ShouldBeNull();
            review.ConfirmedDespiteCodes.ShouldBeEmpty();
        }
    }

    [Fact]
    public void A_confirmation_matches_the_symbol_ignoring_case_and_blanks()
    {
        var c = OwnerConfirmation.Create("owner:hem", "why", " acc-1 ", " aaa ", [ChecklistCodes.FnoBan]).Value;

        Run(f: Facts() with { InFnoBan = null }).Review(c).CanApprove.ShouldBeTrue();
    }

    [Fact]
    public void The_stored_text_names_the_overridden_codes_and_the_reason_and_always_fits_the_column()
    {
        var review = Run(f: Facts() with { InFnoBan = null }).Review(Confirm(ChecklistCodes.FnoBan, ChecklistCodes.Correlation));
        var worst = OwnerConfirmation.Create("owner:hem", new string('x', OwnerConfirmation.MaxReasonLength), "acc-1", "AAA", ChecklistCodes.All.Skip(1)).Value;

        review.ConfirmationText.ShouldBe("overrode fno_ban: checked by hand");
        worst.StoredText(worst.Codes).Length.ShouldBeLessThanOrEqualTo(500);
        Run().Review(Confirm(ChecklistCodes.FnoBan)).ConfirmationText.ShouldBeNull();
    }

    [Theory]
    [InlineData("account_paper")]
    [InlineData("nope")]
    [InlineData("")]
    public void A_confirmation_can_only_name_known_confirmable_checks(string code)
    {
        OwnerConfirmation.Create("owner:hem", "why", "acc-1", "AAA", [code]).Error.Code.ShouldBe("checklist.codes_invalid");
    }

    [Fact]
    public void A_confirmation_needs_at_least_one_code()
    {
        OwnerConfirmation.Create("owner:hem", "why", "acc-1", "AAA", []).Error.Code.ShouldBe("checklist.codes_invalid");
        OwnerConfirmation.Create("owner:hem", "why", "acc-1", "AAA", null).Error.Code.ShouldBe("checklist.codes_invalid");
    }

    [Theory]
    [InlineData(null, "AAA")]
    [InlineData(" ", "AAA")]
    [InlineData("acc-1", null)]
    [InlineData("acc-1", "  ")]
    public void A_confirmation_names_its_account_and_symbol(string? account, string? symbol)
    {
        OwnerConfirmation.Create("owner:hem", "why", account, symbol, [ChecklistCodes.FnoBan]).Error.Code.ShouldBe("checklist.target_required");
    }

    [Fact]
    public void The_owner_prefix_is_defined_once_and_the_actor_needs_a_name_after_it()
    {
        OwnerConfirmation.OwnerPrefix.ShouldBe("owner:");
        OwnerConfirmation.IsOwnerActor("owner:hem").ShouldBeTrue();
        OwnerConfirmation.IsOwnerActor("owner:").ShouldBeFalse();
        OwnerConfirmation.IsOwnerActor("Owner:hem").ShouldBeFalse();
        OwnerConfirmation.IsOwnerActor(null).ShouldBeFalse();
    }

    [Fact]
    public void An_undefined_policy_value_on_a_failed_check_needs_confirmation_and_is_not_warned()
    {
        var odd = new ChecklistCheck(ChecklistCodes.FnoBan, FilterOutcome.Fail, (CheckPolicy)99, null, null, "x");

        odd.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public void A_policy_with_an_undefined_value_or_an_unknown_code_is_refused()
    {
        ChecklistPolicy.Create(new Dictionary<string, CheckPolicy> { [ChecklistCodes.FnoBan] = (CheckPolicy)99 }).Error.Code.ShouldBe("checklist.unknown_policy");
        ChecklistPolicy.Create(new Dictionary<string, CheckPolicy> { ["sector_limit"] = CheckPolicy.Warn }).Error.Code.ShouldBe("checklist.unknown_code");
        ChecklistPolicy.Create(new Dictionary<string, CheckPolicy> { [ChecklistCodes.AccountPaper] = CheckPolicy.Warn }).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_default_policy_follows_doc_16()
    {
        ChecklistPolicy.Default.Of(ChecklistCodes.HoldingsConflict).ShouldBe(CheckPolicy.Warn);
        ChecklistPolicy.Default.Of(ChecklistCodes.SectorExposure).ShouldBe(CheckPolicy.Warn);
        ChecklistPolicy.Default.Of(ChecklistCodes.DataQuality).ShouldBe(CheckPolicy.BlockWithConfirm);
        ChecklistPolicy.Default.Of(ChecklistCodes.ResultsWithinDays).ShouldBe(CheckPolicy.BlockWithConfirm);
    }

    [Theory]
    [InlineData(-1, 1.0, 0.7)]
    [InlineData(31, 1.0, 0.7)]
    [InlineData(3, 0.0, 0.7)]
    [InlineData(3, 100.1, 0.7)]
    [InlineData(3, double.NaN, 0.7)]
    [InlineData(3, 1.0, 0.0)]
    [InlineData(3, 1.0, 1.01)]
    [InlineData(3, 1.0, double.PositiveInfinity)]
    public void Invalid_limits_are_refused(int days, double pct, double corr)
    {
        ChecklistLimits.Create(days, pct, corr).Error.Code.ShouldBe("checklist.invalid_limits");
    }

    [Fact]
    public void Limits_at_their_edges_are_accepted()
    {
        ChecklistLimits.Create(0, 100, 1).IsSuccess.ShouldBeTrue();
        ChecklistLimits.Create(30, 0.001, 0.001).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void A_non_positive_or_non_finite_input_makes_its_check_unavailable(double bad)
    {
        Of(Run(Ticket(risk: bad)), ChecklistCodes.RiskProfile).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(perTrade: bad)), ChecklistCodes.RiskProfile).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(price: bad)), ChecklistCodes.CapitalHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(qty: bad)), ChecklistCodes.CapitalHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(risk: bad)), ChecklistCodes.ExposureHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(total: bad)), ChecklistCodes.ExposureHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(lossLimit: bad)), ChecklistCodes.DailyLoss).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(price: bad)), ChecklistCodes.PriceBand).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { PriceBandLower = bad }), ChecklistCodes.PriceBand).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { PriceBandUpper = bad }), ChecklistCodes.PriceBand).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(Ticket(qty: bad)), ChecklistCodes.Liquidity).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { AverageDailyVolume = bad }), ChecklistCodes.Liquidity).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1.0)]
    public void A_negative_or_non_finite_amount_makes_its_check_unavailable(double bad)
    {
        Of(Run(a: Account(funds: bad)), ChecklistCodes.CapitalHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(openRisk: bad)), ChecklistCodes.ExposureHeadroom).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(loss: bad)), ChecklistCodes.DailyLoss).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public void A_correlation_that_is_not_a_correlation_is_unavailable(double bad)
    {
        Of(Run(f: Facts() with { MaxCorrelation = bad }), ChecklistCodes.Correlation).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void Counts_of_zero_or_below_make_the_sector_check_unavailable_and_so_does_a_negative_day_count()
    {
        Of(Run(a: Account(perSector: 0)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(perSector: -1)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(a: Account(sectorPositions: -1)), ChecklistCodes.SectorExposure).Outcome.ShouldBe(FilterOutcome.Unavailable);
        Of(Run(f: Facts() with { DaysToResults = -1 }), ChecklistCodes.ResultsWithinDays).Outcome.ShouldBe(FilterOutcome.Unavailable);
    }

    [Fact]
    public void At_the_limit_passes_even_when_the_float_sum_is_a_hair_over()
    {
        // 0.2 + 0.1 is 0.30000000000000004 in floating point
        var c = Of(Run(Ticket(risk: 0.1), Account(total: 0.3, openRisk: 0.2)), ChecklistCodes.ExposureHeadroom);

        c.Outcome.ShouldBe(FilterOutcome.Pass);
    }
}
