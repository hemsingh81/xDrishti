using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Application.Tests.Portfolio.Reports;
using XDrishti.Domain.Research;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;

namespace XDrishti.Application.Tests.Research.CostSheets;

/// <summary>
/// CS-01, the rule that a derived sheet never understates a cost: intraday bills are priced by <see cref="CostModel"/> with a known sheet (0.03 % per leg, a cap of 20.00 per leg, a minimum of 5.00 per leg, three rounding rules) at several trade sizes, and the
/// deriver must recover the rate and the cap where the bills prove them, and write null (never a smaller number) where they do not. Hand figures: a leg of 20,000.00 is 6.00 brokerage, of 40,000.00 12.00, of 100,000.00 or more 20.00 (the cap binds from 66,666.67);
/// a leg of 5,000.00 is 1.50 and so 5.00 (the minimum binds below 16,666.67).
/// </summary>
public sealed class DeriveBrokerageTests
{
    private const decimal TrueRate = 0.03m;

    private const decimal TrueCap = 20m;

    private static CostSheet Known(ChargeRounding rounding)
    {
        var intraday = ChargeRates.Create(TrueRate, TrueCap, 5m, 0m, 0.025m, 0.003m, 100m, 0.003m, 0.001m, 0m).Value;
        var delivery = ChargeRates.Create(0m, 0m, 0m, 0.1m, 0.1m, 0.003m, 100m, 0.015m, 0m, 15m).Value;
        var schedule = ChargeSchedule.Create(new DateOnly(2026, 4, 1), 18m, rounding, intraday, delivery).Value;
        return CostSheet.Create("known-sheet", "known", "invented", null, [schedule]).Value;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>One intraday round trip of <paramref name="leg"/> bought and the same sold, its cells priced leg by leg by the model.</summary>
    private static string Bill(CostSheet sheet, int day, decimal leg)
    {
        var date = new DateOnly(2026, 4, 1).AddDays(day - 1);
        var buy = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Buy, leg, date).Value;
        var sell = CostModel.Leg(sheet, CostSegment.IntradayEquity, Side.Sell, leg, date).Value;
        var qty = (long)(leg / 100m);
        return GtrRow(
            date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) + " 00:00:00",
            "ZZ SCRIP",
            "NSE",
            "ZZBILL" + day,
            qty,
            Money(leg),
            qty,
            Money(leg),
            brokerage: Money(buy.Brokerage + sell.Brokerage),
            gst: Money(buy.Gst + sell.Gst),
            stt: Money(buy.Stt + sell.Stt),
            sebi: Money(buy.SebiFee + sell.SebiFee),
            stamp: Money(buy.StampDuty + sell.StampDuty),
            txn: Money(buy.ExchangeTxn + sell.ExchangeTxn),
            other: Money(buy.OtherCharges + sell.OtherCharges));
    }

    private static async Task<CostDerivation> DeriveAsync(ChargeRounding rounding, params decimal[] legs)
    {
        var sheet = Known(rounding);
        var rows = legs.Select((leg, i) => Bill(sheet, i + 1, leg));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 5, 0, 0, TimeSpan.Zero));
        var handler = new DeriveCostSheetHandler(new FakeSpreadsheets(), time);
        var result = await handler.HandleAsync(new DeriveCostSheetQuery([Content(Gtr(rows))], 0.9m), TestContext.Current.CancellationToken);

        return Ok(result);
    }

    private static decimal? Of(CostDerivation derived, string line) => derived.Lines.Single(l => l.Class == CostBillCheck.IntradayName && l.Line == line).Rate;

    /// <summary>The rule: a written brokerage figure is the true one; anything else is null.</summary>
    private static void NeverUnderstates(CostDerivation derived)
    {
        var rate = Of(derived, "brokerage");
        var cap = Of(derived, "brokerage cap");
        (rate is null || rate == TrueRate).ShouldBeTrue("the brokerage rate written is " + rate);
        (cap is null || cap == TrueCap).ShouldBeTrue("the brokerage cap written is " + cap);
    }

    public static TheoryData<ChargeRounding> Roundings => new() { ChargeRounding.HalfAwayFromZero, ChargeRounding.HalfEven, ChargeRounding.Down };

    // 18 bills to derive from, then 2 to check on (0.9 of 20)
    private static readonly decimal[] Mixed =
    [
        20_000m, 40_000m, 100_000m, 200_000m, 500_000m, 20_000m, 40_000m, 100_000m, 200_000m, 500_000m, 20_000m, 40_000m, 100_000m, 200_000m, 500_000m, 20_000m, 40_000m, 100_000m, 20_000m, 40_000m,
    ];

    [Theory]
    [MemberData(nameof(Roundings))]
    public async Task The_rate_and_the_per_leg_cap_are_recovered_where_capped_bills_of_several_sizes_prove_them(ChargeRounding rounding)
    {
        var derived = await DeriveAsync(rounding, Mixed);

        Of(derived, "brokerage").ShouldBe(TrueRate, "taken from the bills below the cap only");
        Of(derived, "brokerage cap").ShouldBe(TrueCap, "per leg: half of the largest whole-bill brokerage of 40.00");
        Of(derived, "stt").ShouldBe(0.025m);
        Of(derived, "txn charges").ShouldBe(0.003m);
        Of(derived, "sebi").ShouldBe(100m);
        Of(derived, "stamp duty").ShouldBe(0.003m);
        Of(derived, "other").ShouldBe(0.001m);
        derived.Lines.Single(l => l.Line == "gst").Rate.ShouldNotBeNull().ShouldBe(18m, 0.1m, "weighted: the sum of the GST over the sum of its bases");
        derived.DeriveSet.Passes.ShouldBeTrue();
        derived.CheckSet.Passes.ShouldBeTrue();
        NeverUnderstates(derived);
    }

    [Fact]
    public async Task Without_a_capped_bill_the_cap_is_null_and_the_rate_is_still_the_true_one()
    {
        var derived = await DeriveAsync(ChargeRounding.HalfAwayFromZero, 20_000m, 40_000m, 20_000m, 40_000m, 20_000m, 40_000m, 20_000m, 40_000m, 20_000m, 40_000m);

        Of(derived, "brokerage cap").ShouldBeNull();
        derived.Json.ShouldContain("\"brokerage_cap\": null");
        Of(derived, "brokerage").ShouldBe(TrueRate);
        NeverUnderstates(derived);
    }

    [Fact]
    public async Task Capped_bills_of_one_size_are_no_proof_of_a_cap()
    {
        // every large bill is 200,000.00 a leg: the same brokerage at the same turnover could as well be a flat fee
        var derived = await DeriveAsync(ChargeRounding.HalfAwayFromZero, 20_000m, 40_000m, 200_000m, 200_000m, 20_000m, 40_000m, 200_000m, 200_000m, 20_000m, 40_000m);

        Of(derived, "brokerage cap").ShouldBeNull();
        derived.Json.ShouldContain("\"brokerage_cap\": null");
        NeverUnderstates(derived);
    }

    [Fact]
    public async Task A_minimum_that_binds_makes_the_rate_unstable_and_it_is_written_null_never_a_lower_median()
    {
        // 5,000.00 a leg is 1.50, charged 5.00 (the minimum): the implied rate of those bills is 0.1 %, of the others 0.03 %
        var derived = await DeriveAsync(ChargeRounding.HalfAwayFromZero, 5_000m, 20_000m, 5_000m, 20_000m, 5_000m, 20_000m, 5_000m, 20_000m, 5_000m, 20_000m);

        var line = derived.Lines.Single(l => l.Class == CostBillCheck.IntradayName && l.Line == "brokerage");
        line.Rate.ShouldBeNull();
        line.Unstable.ShouldBeTrue();
        (line.From, line.To).ShouldBe((new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 8)), "nine bills are derived from; five at the minimum make the median 0.1 and the four bills of 20,000.00 (days 2 to 8) are where it moved");
        derived.Json.ShouldContain("\"brokerage_pct\": null");
        NeverUnderstates(derived);
    }

    [Fact]
    public async Task A_proven_cap_is_written_even_when_the_rate_is_null_and_the_printout_says_so()
    {
        // capped bills of three sizes prove the cap; the small bills at the minimum make the rate unstable
        var derived = await DeriveAsync(ChargeRounding.HalfAwayFromZero, 5_000m, 5_000m, 5_000m, 20_000m, 20_000m, 20_000m, 100_000m, 200_000m, 500_000m, 20_000m);

        Of(derived, "brokerage").ShouldBeNull();
        Of(derived, "brokerage cap").ShouldBe(TrueCap);
        derived.Json.ShouldContain("\"brokerage_pct\": null");
        derived.Json.ShouldContain("\"brokerage_cap\": 20");
        derived.Lines.Single(l => l.Line == "brokerage cap").Note.ShouldNotBeNull().ShouldContain("written to the file even when the rate is not");
        NeverUnderstates(derived);
    }

    [Fact]
    public async Task Bills_that_all_pay_the_minimum_are_not_read_as_a_cap()
    {
        var derived = await DeriveAsync(ChargeRounding.HalfAwayFromZero, 2_000m, 4_000m, 2_000m, 4_000m, 2_000m, 4_000m, 2_000m, 4_000m, 2_000m, 4_000m);

        Of(derived, "brokerage cap").ShouldBeNull();
        Of(derived, "brokerage").ShouldBeNull("the implied rate of 5.00 a leg moves with the size");
        NeverUnderstates(derived);
    }
}
