using System.Reflection;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 2 of P3-08, the dry read repeated by reflection (D4): every positional property of <c>SimTrade</c> and every property of <c>WalkCounters</c> is compared (by the comparator) or checked as an echo (by the adapter), and has
/// a row in the dry-read table <c>docs/research/p3-08-field-definitions.md</c> whose status is not "no sentence" without an ADR 0047 rule; a field added later without a row fails here, so it cannot escape the comparison unread.
/// </summary>
public sealed class FieldCoverageTests
{
    private static readonly string[] AllowedStatus = ["DEFINED", "SPEC ONLY", "PASS-THROUGH", "NONE"];

    /// <summary>The names of the positional properties of a record (its primary constructor's parameters).</summary>
    internal static IReadOnlyList<string> Positional(Type record) =>
        [.. record.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters().Select(p => p.Name!)];

    /// <summary>The properties of <paramref name="properties"/> that no source in <paramref name="covered"/> names (what the check reports).</summary>
    internal static List<string> Unread(IEnumerable<string> properties, IEnumerable<string> covered) => [.. properties.Except(covered, StringComparer.Ordinal)];

    /// <summary>A row of the table: the field, the status word and the ADR 0047 column.</summary>
    internal sealed record Row(string Field, string Status, string Adr);

    internal static IReadOnlyList<Row> Table()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Oracle", "Docs", "p3-08-field-definitions.md");
        var rows = new List<Row>();
        foreach (var line in File.ReadLines(path).Where(l => l.StartsWith("| ", StringComparison.Ordinal)))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            // | # | `Field` | text | status | ADR |  (the split leaves an empty first and last cell)
            if (cells.Length < 7 || !cells[2].StartsWith('`') || !int.TryParse(cells[1], System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            rows.Add(new Row(cells[2].Trim('`'), cells[4], cells[5]));
        }

        return rows;
    }

    [Fact]
    public void Every_positional_property_of_SimTrade_is_compared_or_checked_as_an_echo()
    {
        var covered = OracleComparer.TradeFields.Concat(OracleAdapter.EchoFields);

        Unread(Positional(typeof(SimTrade)), covered).ShouldBeEmpty("a SimTrade field that no comparison reads");
        Positional(typeof(SimTrade)).Count.ShouldBe(34);
    }

    [Fact]
    public void Every_property_of_WalkCounters_is_compared()
    {
        var properties = typeof(WalkCounters).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").Select(p => p.Name);

        Unread(properties, OracleComparer.CounterFields).ShouldBeEmpty("a WalkCounters counter that no comparison reads");
        Positional(typeof(WalkCounters)).Count.ShouldBe(32);
    }

    [Fact]
    public void Every_field_has_a_row_in_the_dry_read_table_and_no_row_is_left_without_a_sentence_or_a_rule()
    {
        var table = Table();
        var names = table.Select(r => r.Field).ToList();

        Unread(Positional(typeof(SimTrade)), names).ShouldBeEmpty("a SimTrade field with no row in the dry-read table");
        Unread(Positional(typeof(WalkCounters)), names).ShouldBeEmpty("a WalkCounters counter with no row in the dry-read table");
        foreach (var row in table)
        {
            AllowedStatus.Any(w => row.Status.StartsWith(w, StringComparison.Ordinal)).ShouldBeTrue($"the status of {row.Field}: {row.Status}");
            if (row.Status.StartsWith("NONE", StringComparison.Ordinal))
            {
                row.Adr.ShouldNotBe("-", $"{row.Field} has no sentence and no rule in ADR 0047");
            }
        }
    }

    [Fact]
    public void The_sentinel_a_field_added_without_a_row_is_reported()
    {
        var withNewField = Positional(typeof(SimTrade)).Append("FutureField");

        Unread(withNewField, OracleComparer.TradeFields.Concat(OracleAdapter.EchoFields)).ShouldBe(["FutureField"]);
        Unread(withNewField, Table().Select(r => r.Field)).ShouldBe(["FutureField"]);
    }

    [Fact]
    public void Every_echo_field_is_really_checked_a_doctored_trade_is_reported_field_by_field()
    {
        var day = SeededDays.All.First(d => d.Walks.Any(w => TradeWalker.Walk(w.Candidate, d.Bars, w.Rules).Value.Trade is not null));
        var walk = day.Walks.First(w => TradeWalker.Walk(w.Candidate, day.Bars, w.Rules).Value.Trade is not null);
        var trade = TradeWalker.Walk(walk.Candidate, day.Bars, walk.Rules).Value.Trade!;
        OracleAdapter.Echo(trade, walk.Candidate, walk.Rules).ShouldBeEmpty();

        var doctored = new Dictionary<string, SimTrade>
        {
            [nameof(SimTrade.SchemaVersion)] = trade with { SchemaVersion = 3 },
            [nameof(SimTrade.StrategyId)] = trade with { StrategyId = "OTHER" },
            [nameof(SimTrade.InstrumentId)] = trade with { InstrumentId = Guid.NewGuid() },
            [nameof(SimTrade.Symbol)] = trade with { Symbol = "OTHER" },
            [nameof(SimTrade.Side)] = trade with { Side = trade.Side == Side.Buy ? Side.Sell : Side.Buy },
            [nameof(SimTrade.TriggerTimeframe)] = trade with { TriggerTimeframe = Timeframe.H1 },
            [nameof(SimTrade.TriggerBar)] = trade with { TriggerBar = trade.TriggerBar with { Close = trade.TriggerBar.Close + 1 } },
            [nameof(SimTrade.TriggerBarMinutes)] = trade with { TriggerBarMinutes = trade.TriggerBarMinutes + 1 },
            [nameof(SimTrade.TriggerBarComplete)] = trade with { TriggerBarComplete = !trade.TriggerBarComplete },
            [nameof(SimTrade.EntrySnapshotJson)] = trade with { EntrySnapshotJson = "{}" },
            [nameof(SimTrade.Costs)] = trade with { Costs = CostBasis.Charged },
            [nameof(SimTrade.Costing)] = trade with { Costing = AnyCosting() },
            [nameof(SimTrade.TickFallback)] = trade with { TickFallback = !trade.TickFallback },
        };

        doctored.Keys.Order().ShouldBe(OracleAdapter.EchoFields.Order());
        foreach (var (field, changed) in doctored)
        {
            OracleAdapter.Echo(changed, walk.Candidate, walk.Rules).Select(m => m.Field).ShouldContain(field, $"a changed {field} was not noticed");
        }
    }

    internal static TradeCosting AnyCosting() => new(CostSegment.IntradayEquity, "sheet", 1, 1m, 1m, 1m, LegCharges.Zero, LegCharges.Zero, 0m, 0.0, 0.0);
}
