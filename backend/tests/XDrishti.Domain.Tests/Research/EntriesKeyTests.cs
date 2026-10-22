using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criterion 8 of P4-05: the entries key excludes the exit and includes everything else; each input is changed in turn. The comparison key names its members and their order.</summary>
public sealed class EntriesKeyTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000001");

    private const string Entries = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";

    private static RunHeader Header(string definition = "1", string engine = "sim-6", double or = 15, DateOnly? to = null, string fills = "{}", string label = UniverseLabels.InstrumentsAsListed, bool ack = false, ReplayOptions? options = null, Timeframe tf = Timeframe.M15, CostRecord? costs = null)
    {
        var strategy = new StrategyRecord("ORB", new Dictionary<string, double> { ["or_min"] = or }, "{}", new string(definition[0], 64), tf, [Side.Buy]);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, label, [new UniverseMember(Id, "X")]);
        return new RunHeader(strategy, engine, Monday, to ?? Monday.AddDays(4), options ?? ReplayOptions.Default, ack, fills, universe, new TradingCalendar([]), costs);
    }

    private static InstrumentRun Instrument(string hash) => new(
        Id, "X", null, null, Monday, Monday, Monday, new DateSpan(Monday, Monday), 0, 0, 0, 0, hash, 0.05m, false, [], SessionCounts.Empty, [], false, [], [], WalkCounters.Empty, RunFates.Empty, 0, 0);

    private static string Key(RunHeader header, string entries = Entries, string bars = "b")
    {
        var run = RunAssembler.Assemble(header, [new InstrumentReplay(Instrument(new string(bars[0], 64)), [], [])]);
        return RunAssembler.EntriesKey(header, run, entries);
    }

    [Fact]
    public void The_entries_key_does_not_depend_on_the_definition_hash_that_includes_the_exit()
    {
        Key(Header(definition: "1")).ShouldBe(Key(Header(definition: "2")));
        RunAssembler.Assemble(Header(definition: "1"), [new InstrumentReplay(Instrument(new string('b', 64)), [], [])]).RunKey
            .ShouldNotBe(RunAssembler.Assemble(Header(definition: "2"), [new InstrumentReplay(Instrument(new string('b', 64)), [], [])]).RunKey, "the run key does");
    }

    [Fact]
    public void The_entries_key_is_not_the_run_key_and_is_stable()
    {
        var header = Header();
        var run = RunAssembler.Assemble(header, [new InstrumentReplay(Instrument(new string('b', 64)), [], [])]);

        Key(header).ShouldBe(Key(header));
        Key(header).ShouldNotBe(run.RunKey);
        Key(header).Length.ShouldBe(64);
    }

    public static TheoryData<string> Changes => ["entries definition", "parameter", "window to", "fills", "universe label", "acknowledgement", "options", "engine", "timeframe", "bars", "costs"];

    [Theory]
    [MemberData(nameof(Changes))]
    public void The_entries_key_changes_when_anything_but_the_exit_changes(string change)
    {
        var baseline = Key(Header());
        var sheet = new CostRecord("test-a", new string('a', 64), "{}", null, 100_000m, SizingProfile.Standard, 1m);

        var changed = change switch
        {
            "entries definition" => Key(Header(), entries: "f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2"),
            "parameter" => Key(Header(or: 30)),
            "window to" => Key(Header(to: Monday.AddDays(5))),
            "fills" => Key(Header(fills: "{\"x\":1}")),
            "universe label" => Key(Header(label: UniverseLabels.MembersAsOfToday)),
            "acknowledgement" => Key(Header(ack: true)),
            "options" => Key(Header(options: new ReplayOptions(0.95, 0.20))),
            "engine" => Key(Header(engine: "sim-7")),
            "timeframe" => Key(Header(tf: Timeframe.M5)),
            "bars" => Key(Header(), bars: "c"),
            _ => Key(Header(costs: sheet)),
        };

        changed.ShouldNotBe(baseline, change);
    }

    [Fact]
    public void The_comparison_key_names_the_members_and_their_order()
    {
        SimulationRun Run(string definition)
        {
            var header = Header(definition: definition);
            return RunAssembler.Assemble(header, [new InstrumentReplay(Instrument(new string('b', 64)), [], [])]);
        }

        var exit = new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [2])]);
        var report = ComparisonReport.Build([new CandidateRun("a", exit, Run("1"), 0)], true);
        var forward = new ComparisonRun([new CandidateRun("a", exit, Run("1"), 0), new CandidateRun("b", exit, Run("2"), 0)], report, Entries);
        var backward = new ComparisonRun([new CandidateRun("b", exit, Run("2"), 0), new CandidateRun("a", exit, Run("1"), 0)], report, Entries);
        var other = forward with { EntriesKey = "f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2" };

        forward.ComparisonKey.ShouldBe(forward.ComparisonKey);
        forward.ComparisonKey.ShouldNotBe(backward.ComparisonKey);
        forward.ComparisonKey.ShouldNotBe(other.ComparisonKey);
    }
}
