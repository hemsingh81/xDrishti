using System.Reflection;
using System.Text.RegularExpressions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>
/// P3-10, risk "a wide mapping that drifts from <c>SimTrade</c>": the column list of <c>research.sim_trades</c> is compared with the public properties of <see cref="SimTrade"/>, <see cref="Bar"/>, <see cref="TradeCosting"/> and
/// <see cref="LegCharges"/>, so a field added to the Domain, and the raising of a schema version, fail here until the mapping and a migration are written.
/// </summary>
public sealed partial class TradeColumnsTests
{
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=^[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex Humps();

    private static string Snake(string name) => Humps().Replace(name, "_").ToLowerInvariant();

    private static IEnumerable<string> Properties(Type type, params string[] except) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Where(n => !except.Contains(n, StringComparer.Ordinal));

    /// <summary>The names the Domain implies, written by rule from the types, with the few renames the spec fixes. The legs of a trade and the charges of its legs (P4-01) are left out on purpose: the table has no column for them yet, and a trade with legs is refused before the store (D15).</summary>
    private static List<string> ExpectedNames()
    {
        var names = Properties(typeof(SimTrade), nameof(SimTrade.SchemaVersion), nameof(SimTrade.TriggerBar), nameof(SimTrade.Costing), nameof(SimTrade.Key), nameof(SimTrade.Legs)).Select(Snake).ToList();
        names.AddRange(Properties(typeof(Bar), nameof(Bar.Start)).Select(n => n == nameof(Bar.EpochSeconds) ? "trigger_bar_epoch" : "trigger_bar_" + Snake(n)));
        names.AddRange(Properties(typeof(TradeCosting), nameof(TradeCosting.SheetId), nameof(TradeCosting.EntryLeg), nameof(TradeCosting.ExitLeg), nameof(TradeCosting.LegCharges)).Select(n => n switch
        {
            nameof(TradeCosting.TotalCost) => "cost_total",
            nameof(TradeCosting.CostR) => "cost_r",
            nameof(TradeCosting.RNet) => "r_net",
            _ => "cost_" + Snake(n),
        }));
        foreach (var leg in new[] { "entry", "exit" })
        {
            names.AddRange(Properties(typeof(LegCharges), nameof(LegCharges.Total)).Select(n => leg + "_" + Snake(n)));
        }

        names.Add("basket_ids");
        return names;
    }

    [Fact]
    public void The_columns_are_the_fields_of_the_trade_the_bar_the_costing_and_its_two_legs_and_the_baskets()
    {
        TradeColumns.Columns.Select(c => c.Name).Order(StringComparer.Ordinal).ShouldBe(ExpectedNames().Order(StringComparer.Ordinal));
        TradeColumns.Columns.Count.ShouldBe(61, "61 mapped columns and the four keys are the 65 of the spec");
    }

    [Fact]
    public void The_schema_versions_the_table_is_built_for_are_pinned()
    {
        SimTrade.CurrentSchemaVersion.ShouldBe(5, "schema 5 (P4-01, ADR 0049) only appends the legs, which this table does not hold yet: a trade with legs is refused before the store, and a trade without legs equals its schema-4 form, so the columns stay (any later change needs new columns, a migration and an ADR)");
        SimulationRun.CurrentSchemaVersion.ShouldBe(6);
    }

    [Fact]
    public void The_column_names_are_unique_and_the_24_costing_columns_are_the_last_ones_and_the_only_nullable_ones_but_two()
    {
        TradeColumns.Columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count().ShouldBe(TradeColumns.Columns.Count);
        TradeColumns.CostingNames.Count.ShouldBe(24);
        TradeColumns.CostingNames.ShouldBe(TradeColumns.Columns.TakeLast(24).Select(c => c.Name));
        TradeColumns.CostingNames.ShouldAllBe(n => TradeColumns.Columns.Single(c => c.Name == n).Nullable);
        TradeColumns.Columns.Where(c => c.Nullable && !TradeColumns.CostingNames.Contains(c.Name)).Select(c => c.Name).Order().ShouldBe(["entry_snapshot_json", "target_price"]);
    }

    [Fact]
    public void The_enum_columns_carry_the_lower_case_names_of_their_enum()
    {
        TradeColumns.Columns.Where(c => c.AllowedNames is not null).Select(c => c.Name).Order().ShouldBe(["cost_segment", "costs", "entry_fill", "exit_reason", "fills", "side", "trigger_timeframe"]);
        TradeColumns.Columns.Single(c => c.Name == "exit_reason").AllowedNames.ShouldBe(["stop", "target", "squareoff", "endofdata", "maxhold"]);
        TradeColumns.Columns.Single(c => c.Name == "trigger_timeframe").AllowedNames.ShouldBe(["m1", "m5", "m10", "m15", "m30", "h1", "d1", "w1", "mo1"]);
    }

    [Fact]
    public void An_enum_name_that_the_enum_does_not_have_and_a_number_are_refused()
    {
        Should.Throw<XDrishti.Application.Abstractions.Research.StoreReadException>(() => EnumText<Side>.Parse("sideways"));
        Should.Throw<XDrishti.Application.Abstractions.Research.StoreReadException>(() => EnumText<Side>.Parse("1"));
        EnumText<Side>.Parse("sell").ShouldBe(Side.Sell);
        EnumText<ExitReason>.Parse("squareoff").ShouldBe(ExitReason.SquareOff);
    }

    [Fact]
    public void A_cursor_round_trips_and_a_foreign_or_damaged_one_is_refused()
    {
        var at = new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero).AddTicks(10);

        PageCursor.TryReadRun(PageCursor.ForRun(at, 42), out var createdAt, out var id).ShouldBeTrue();
        (createdAt, id).ShouldBe((at, 42L));
        PageCursor.TryReadTrade(PageCursor.ForTrade(true, 7), out var open, out var ordinal).ShouldBeTrue();
        (open, ordinal).ShouldBe((true, 7));
        PageCursor.TryReadTrade(PageCursor.ForRun(at, 42), out _, out _).ShouldBeFalse();
        PageCursor.TryReadRun(PageCursor.ForTrade(false, 1), out _, out _).ShouldBeFalse();
        PageCursor.TryReadTrade("!!!", out _, out _).ShouldBeFalse();
        PageCursor.TryReadTrade(new string('A', 200), out _, out _).ShouldBeFalse();
    }
}
