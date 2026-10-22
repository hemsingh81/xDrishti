using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>
/// Stored strategies (spec P3-02d): the embedded seeds read to exactly the definitions the builders make (D6, criteria 3 and 4), the stored path of the engine adapter prepares the same run record as the built-in path (criterion 8), takes values
/// for declared parameters only (criterion 11) and refuses a text that does not give its stored hash (criterion 10).
/// </summary>
public sealed class StoredStrategyTests
{
    /// <summary>The hash of each built-in after rr became a declared parameter (ADR 0056 D5), computed from the canonical text of the builders; the second value is the hash before, which old stored runs keep in their keys.</summary>
    private static readonly Dictionary<string, (string Now, string Before)> Hashes = new()
    {
        ["ORB"] = ("0ba11bc69d2f1cfa24d8801f78f8fcc111415b39fa1454a1a09e59eeebf8574c", "9c1141cf1835d03a0b25b280bbab9b2a7367db8e13ec7568cb0083ca9db14e03"),
        ["VWAP_PULLBACK"] = ("82f2d536dd1f8879c36620226ae00466fcca73f65a129e110ed276595f23a401", "7c1c2493b220e3c343c50960b08a623d48be346e198c7bc233710aaccbe7cca9"),
        ["BASE_BREAKOUT"] = ("68c445de513df1e644de3dcae920629c0cf131ad6a6e26d8a2107dbffd7d074b", "c41e6a82171fbef80953512750c2eec2a300c461893d6ff627a936956b585374"),
    };

    private const string Custom = """
        strategy:
          schema: 1
          id: SWING_X
          style: intraday
          timeframes:
            - 15m
          sides:
            - BUY
          params:
            swing_lookback: 5
          trigger:
            all:
              - "close > or_high(15) and close[1] <= or_high(15)"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: swing_lookback
          exits:
            - policy: FIXED_RR
              rr: [2]
          time_rules:
            first_entry: "09:30"
            last_entry: "11:30"
            square_off: "15:15"
        """;

    private static readonly SimulationEngineAdapter Engine = new();

    private static string Seed(string id) => StrategySeeder.EmbeddedTexts().Single(t => t.Name == id + ".yaml").Text;

    private static StrategyDefinition Builder(string id) => id switch
    {
        "ORB" => OrbV1.Create(),
        "VWAP_PULLBACK" => VwapPullback.Create(),
        _ => BaseBreakoutV1.Create(),
    };

    private static Timeframe Frame(string id) => id == "BASE_BREAKOUT" ? Timeframe.D1 : Timeframe.M15;

    private static Dictionary<string, double> Params(params (string Name, double Value)[] values) => values.ToDictionary(v => v.Name, v => v.Value);

    [Fact]
    public void The_three_built_ins_are_embedded_as_text()
    {
        StrategySeeder.EmbeddedTexts().Select(t => t.Name).ShouldBe(["BASE_BREAKOUT.yaml", "ORB.yaml", "VWAP_PULLBACK.yaml"]);
    }

    [Theory]
    [InlineData("ORB")]
    [InlineData("VWAP_PULLBACK")]
    [InlineData("BASE_BREAKOUT")]
    public void A_seed_reads_to_the_definition_of_its_builder_and_has_the_pinned_hash(string id)
    {
        var read = StrategyFile.Read(Seed(id));

        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        read.Definition.ShouldBe(Builder(id));
        StrategyHash.Of(read.Definition!).ShouldBe(Hashes[id].Now);
        BuiltInStrategies.Prepare(id, null, Frame(id)).Strategy.ShouldNotBeNull().Record.DefinitionSha256.ShouldBe(Hashes[id].Now);
        Hashes[id].Now.ShouldNotBe(Hashes[id].Before, "declaring rr changed the text once (ADR 0056 D5)");
    }

    [Theory]
    [InlineData("ORB")]
    [InlineData("VWAP_PULLBACK")]
    [InlineData("BASE_BREAKOUT")]
    public void A_seed_compiles_on_every_timeframe_it_declares_with_no_problem_at_all(string id)
    {
        var described = Engine.Describe(Seed(id));

        described.IsSuccess.ShouldBeTrue(described.IsFailure ? string.Join("; ", described.Error.Fields!.Select(f => f.Field + " " + f.Code)) : string.Empty);
        described.Value.Id.ShouldBe(id);
        described.Value.Hash.ShouldBe(Hashes[id].Now);
        described.Value.CanonicalJson.ShouldBe(CanonicalStrategy.Json(Builder(id)));
    }

    [Theory]
    [InlineData("ORB")]
    [InlineData("VWAP_PULLBACK")]
    [InlineData("BASE_BREAKOUT")]
    public void The_stored_path_gives_the_run_record_of_the_built_in_path(string id)
    {
        var stored = Engine.PrepareStored(Seed(id), Hashes[id].Now, Params(), Frame(id), null);
        var builtIn = Engine.Prepare(id, Params(), Frame(id), null);

        stored.IsSuccess.ShouldBeTrue();
        builtIn.IsSuccess.ShouldBeTrue();
        stored.Value.Record.ShouldBe(builtIn.Value.Record, new RecordComparer());
        stored.Value.Candidates.Select(c => c.Record).ShouldBe(builtIn.Value.Candidates.Select(c => c.Record), new RecordComparer());
    }

    [Fact]
    public void A_value_for_a_declared_parameter_is_the_value_of_the_record_and_of_the_definition_and_changes_the_hash_of_the_run_only_through_the_parameters()
    {
        var stored = Engine.PrepareStored(Seed("ORB"), Hashes["ORB"].Now, Params(("or_min", 30), ("rr", 3)), Timeframe.M15, null);
        var builtIn = Engine.Prepare("ORB", Params(("or_min", 30), ("rr", 3)), Timeframe.M15, null);

        stored.IsSuccess.ShouldBeTrue();
        stored.Value.Record.Parameters.ShouldBe(new Dictionary<string, double> { ["or_min"] = 30, ["rr"] = 3 });
        stored.Value.Record.ShouldBe(builtIn.Value.Record, new RecordComparer());
        stored.Value.Record.DefinitionJson.ShouldNotBe(Engine.PrepareStored(Seed("ORB"), Hashes["ORB"].Now, Params(), Timeframe.M15, null).Value.Record.DefinitionJson, "the value is part of the text the run records");
    }

    [Fact]
    public void A_custom_text_takes_a_value_for_its_declared_parameter_and_refuses_a_name_it_does_not_declare()
    {
        var described = Engine.Describe(Custom);
        var hash = described.Value.Hash;

        var seven = Engine.PrepareStored(Custom, hash, Params(("swing_lookback", 7)), Timeframe.M15, null);
        var five = Engine.PrepareStored(Custom, hash, Params(), Timeframe.M15, null);
        var unknown = Engine.PrepareStored(Custom, hash, Params(("nonexistent", 1)), Timeframe.M15, null);

        described.IsSuccess.ShouldBeTrue();
        seven.IsSuccess.ShouldBeTrue();
        seven.Value.Record.Parameters.ShouldBe(new Dictionary<string, double> { ["swing_lookback"] = 7 });
        five.Value.Record.Parameters.ShouldBe(new Dictionary<string, double> { ["swing_lookback"] = 5 });
        seven.Value.Record.DefinitionSha256.ShouldNotBe(five.Value.Record.DefinitionSha256, "the substituted value is in the text that is hashed");
        unknown.IsFailure.ShouldBeTrue();
        unknown.Error.Fields.ShouldNotBeNull().Select(f => (f.Field, f.Code)).ShouldBe([("parameters.nonexistent", SimulationErrors.UnknownParameterCode)]);
    }

    [Fact]
    public void A_text_that_does_not_give_the_stored_hash_is_never_run()
    {
        var tampered = Seed("ORB").Replace("or_min: 15", "or_min: 45", StringComparison.Ordinal);

        var result = Engine.PrepareStored(tampered, Hashes["ORB"].Now, Params(), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("strategy_store.hash_mismatch");
    }

    [Fact]
    public void A_reader_that_changes_how_an_old_text_reads_is_the_same_mismatch()
    {
        // the simulated change: the store holds the hash of a text as an older reader read it
        var result = Engine.PrepareStored(Seed("ORB"), Hashes["ORB"].Before, Params(), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("strategy_store.hash_mismatch");
    }

    [Fact]
    public void A_text_that_no_longer_reads_is_the_same_mismatch_and_nothing_runs()
    {
        var result = Engine.PrepareStored("not: [a strategy", Hashes["ORB"].Now, Params(), Timeframe.M15, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("strategy_store.hash_mismatch");
    }

    [Fact]
    public void The_exits_of_a_choice_replace_the_exit_of_a_stored_strategy_as_they_do_for_a_built_in()
    {
        var exits = ExitFlags.Parse(["FIXED_RR:rr=3"], null, []).ShouldNotBeNull();

        var stored = Engine.PrepareStored(Seed("ORB"), Hashes["ORB"].Now, Params(), Timeframe.M15, null, exits);
        var builtIn = Engine.Prepare("ORB", Params(), Timeframe.M15, null, exits);

        stored.IsSuccess.ShouldBeTrue();
        stored.Value.Candidates.Select(c => (c.Id, c.Record.DefinitionSha256)).ShouldBe(builtIn.Value.Candidates.Select(c => (c.Id, c.Record.DefinitionSha256)));
    }

    [Fact]
    public void A_bad_text_is_refused_by_the_description_with_located_fields()
    {
        var result = Engine.Describe(Seed("ORB").Replace("or_min: 15", "or_min: 20", StringComparison.Ordinal));

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.ShouldNotBeNull().ShouldNotBeEmpty();
        result.Error.Fields.ShouldAllBe(f => f.Field.Length > 0 && f.Code != null);
    }

    [Fact]
    public async Task The_seeder_stores_each_text_that_passes_with_the_seed_flag_and_skips_a_bad_one()
    {
        var store = new RecordingStore();
        var seeder = new StrategySeeder(store, Engine, Microsoft.Extensions.Logging.Abstractions.NullLogger<StrategySeeder>.Instance);

        var stored = await seeder.SeedAsync([("ORB.yaml", Seed("ORB")), ("BAD.yaml", "id: nothing"), ("VWAP_PULLBACK.yaml", Seed("VWAP_PULLBACK"))], TestContext.Current.CancellationToken);

        stored.ShouldBe(2);
        store.Appended.Select(a => (a.StrategyId, a.Seed, a.Hash)).ShouldBe([("ORB", true, Hashes["ORB"].Now), ("VWAP_PULLBACK", true, Hashes["VWAP_PULLBACK"].Now)]);
        store.Appended.ShouldAllBe(a => a.SourceText.Length > 0 && a.CanonicalJson.Length > 0 && a.Style == "intraday");
    }

    [Fact]
    public async Task The_seeder_does_not_count_a_text_the_store_refuses()
    {
        var store = new RecordingStore { Refuse = true };
        var seeder = new StrategySeeder(store, Engine, Microsoft.Extensions.Logging.Abstractions.NullLogger<StrategySeeder>.Instance);

        (await seeder.SeedAsync(StrategySeeder.EmbeddedTexts(), TestContext.Current.CancellationToken)).ShouldBe(0);
        store.Appended.Count.ShouldBe(3);
    }

    private sealed class RecordingStore : IStrategyStore
    {
        public List<NewVersion> Appended { get; } = [];

        public bool Refuse { get; init; }

        public ValueTask<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<StoredStrategy?> GetAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<StoredVersion?> GetVersionAsync(string id, int? version, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<StoredVersion?> GetByHashAsync(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<AppendResult>> AppendAsync(NewVersion version, CancellationToken cancellationToken)
        {
            Appended.Add(version);
            return ValueTask.FromResult(Refuse
                ? Result.Failure<AppendResult>(StrategyStoreErrors.BuiltInReadOnly(version.StrategyId))
                : Result.Success(new AppendResult(version.StrategyId, 1, version.Hash, true)));
        }
    }

    /// <summary>Compares the run records by their fields: the parameters are dictionaries, which records compare by reference.</summary>
    private sealed class RecordComparer : IEqualityComparer<StrategyRecord>
    {
        public bool Equals(StrategyRecord? x, StrategyRecord? y) =>
            x is not null && y is not null && x.Id == y.Id && x.DefinitionJson == y.DefinitionJson && x.DefinitionSha256 == y.DefinitionSha256 && x.EvaluationTimeframe == y.EvaluationTimeframe
            && x.Sides.SequenceEqual(y.Sides) && x.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(y.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal));

        public int GetHashCode(StrategyRecord obj) => obj.DefinitionSha256.GetHashCode(StringComparison.Ordinal);
    }
}
