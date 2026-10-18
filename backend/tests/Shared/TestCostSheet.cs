using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// The TEST charge sheet of P3-03c (spec D16, ADR 0042): <b>synthetic numbers, compiled into the test projects only</b>, never into a <c>src/</c> assembly (an architecture test looks). Id <c>test-synthetic-1</c>, so
/// <see cref="CostSheet.IsTest"/> is true. Brokerage 0.03 %, cap 20, delivery brokerage 0 and GST 18 % are the values of doc 16 (their being current is unverified); every other figure is invented round value, chosen so that every line is
/// non-zero and a hand calculation is short, and is not the rate of any exchange or broker. Two schedules, to test the date rule: A from 2026-01-01 and B from 2026-03-03 (the intraday sell STT goes from 0.04 to 0.035, the delivery
/// sell STT from 0.08 to 0.09). Rounding half away from zero. Nothing is read from <c>data/</c>.
/// </summary>
internal static class TestCostSheet
{
    public const string Id = "test-synthetic-1";

    public static readonly DateOnly ScheduleA = new(2026, 1, 1);

    public static readonly DateOnly ScheduleB = new(2026, 3, 3);

    /// <summary>The canonical text of the sheet.</summary>
    public static string Json => Sheet.ToCanonicalJson();

    /// <summary>The context of the worked examples: the sheet, the standard profile, capital 10,00,000 and <c>max_cost_R</c> 0.10.</summary>
    public static CostContext Context(decimal capital = 1_000_000m, decimal maxCostR = 0.10m, SizingProfile? profile = null, CostSheet? sheet = null) =>
        CostContext.Create(sheet ?? Sheet, profile ?? SizingProfile.Standard, capital, maxCostR).Value;

    /// <summary>The rates of the intraday segment in force from <see cref="ScheduleA"/>.</summary>
    public static ChargeRates IntradayA { get; } = Rates(0.03m, 20m, 0m, 0m, 0.04m, 0.005m, 50m, 0.02m, 0m, 0m);

    /// <summary>The rates of the intraday segment in force from <see cref="ScheduleB"/>.</summary>
    public static ChargeRates IntradayB { get; } = Rates(0.03m, 20m, 0m, 0m, 0.035m, 0.005m, 50m, 0.02m, 0m, 0m);

    /// <summary>The rates of the delivery segment in force from <see cref="ScheduleA"/>.</summary>
    public static ChargeRates DeliveryA { get; } = Rates(0m, 0m, 0m, 0.08m, 0.08m, 0.004m, 50m, 0.03m, 0m, 10m);

    /// <summary>The rates of the delivery segment in force from <see cref="ScheduleB"/>.</summary>
    public static ChargeRates DeliveryB { get; } = Rates(0m, 0m, 0m, 0.08m, 0.09m, 0.004m, 50m, 0.03m, 0m, 10m);

    /// <summary>The sheet. (Declared after the rates it is built from: static initialisers run in text order.)</summary>
    public static CostSheet Sheet { get; } = Build();

    /// <summary>Rates from ten numbers; the test fails with the problems when they are not valid.</summary>
    public static ChargeRates Rates(decimal brokeragePct, decimal brokerageCap, decimal brokerageMin, decimal sttBuy, decimal sttSell, decimal exchange, decimal sebi, decimal stamp, decimal other, decimal dp) =>
        ChargeRates.Create(brokeragePct, brokerageCap, brokerageMin, sttBuy, sttSell, exchange, sebi, stamp, other, dp).Value;

    /// <summary>A schedule from its parts; the test fails when they are not valid.</summary>
    public static ChargeSchedule Schedule(DateOnly from, ChargeRates intraday, ChargeRates delivery, decimal gst = 18m, ChargeRounding rounding = ChargeRounding.HalfAwayFromZero) =>
        ChargeSchedule.Create(from, gst, rounding, intraday, delivery).Value;

    /// <summary>A sheet of one or more schedules; the test fails when it is not valid.</summary>
    public static CostSheet Of(string id, params ChargeSchedule[] schedules) => CostSheet.Create(id, "TEST sheet (synthetic numbers)", "invented for tests; not a broker's sheet", null, schedules).Value;

    /// <summary>The TEST sheet with another rounding rule in both schedules.</summary>
    public static CostSheet WithRounding(ChargeRounding rounding) =>
        Of(Id, Schedule(ScheduleA, IntradayA, DeliveryA, rounding: rounding), Schedule(ScheduleB, IntradayB, DeliveryB, rounding: rounding));

    private static CostSheet Build() =>
        CostSheet.Create(
            Id,
            "TEST sheet (synthetic numbers)",
            "invented for tests; not a broker's sheet",
            null,
            [Schedule(ScheduleA, IntradayA, DeliveryA), Schedule(ScheduleB, IntradayB, DeliveryB)]).Value;
}
