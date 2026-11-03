using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Pairs;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Application.Tests.Pairs;

/// <summary>P14-13 criteria 1, 5, 7 and 10 at the use case: the ledger is read first (the primary variant must be a counted trial), N and F come from it, the record is protocol S with fold unit quarter, a re-run stores nothing new, the hold-out is read once.</summary>
public sealed class EvaluatePairsHandlerTests
{
    private static readonly string Config = PairConfig.Of(PairStudyOptions.Default, null);

    private readonly Journal _journal = new();
    private readonly FakeLedger _ledger;
    private readonly FakeStore _store;
    private readonly FakeEvaluations _evaluations = new();
    private readonly FakeSettings _settings = new();
    private readonly FakeSimulator _simulator;
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    public EvaluatePairsHandlerTests()
    {
        _ledger = new FakeLedger(_journal);
        _store = new FakeStore(_journal);
        _simulator = new FakeSimulator(_journal);
    }

    private EvaluatePairsHandler Handler()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return new EvaluatePairsHandler(_store, _ledger, _evaluations, new EvaluationWriter(_evaluations, _ledger, time), _settings, _sheets, _simulator, NullLogger<EvaluatePairsHandler>.Instance);
    }

    private static StoredFormation Stored(string quarter, string idPrefix, int minutes, PairFormationStat stat) =>
        new(idPrefix.PadRight(64, '0'), new DateTimeOffset(2026, 1, 1, 0, minutes, 0, TimeSpan.Zero), stat with { Quarter = quarter });

    private async Task LedgerAsync(int trials, bool includePrimary = true)
    {
        var from = new DateOnly(2024, 1, 1);
        var primary = PairTrial.Variant(PairVariant.Primary, PairStudyOptions.Default, from, from);
        var made = 0;
        if (includePrimary)
        {
            await _ledger.AppendAsync(new LedgerRow(primary.RunId, "pairs-variant", primary.Family, 1, primary.Hash, null, "stored bars", from, from, "pairs-variant", DateTimeOffset.UnixEpoch), CancellationToken.None);
            made++;
        }

        for (; made < trials; made++)
        {
            var hash = ExtensionFamilies.Sha256Hex("other" + made.ToString(CultureInfo.InvariantCulture));
            await _ledger.AppendAsync(new LedgerRow(Guid.NewGuid(), "pairs-formation", PairFamilies.Intraday, 1, hash, null, "stored bars", from, from, "pairs-formation", DateTimeOffset.UnixEpoch), CancellationToken.None);
        }
    }

    private void Trades(ulong seed, double mean, int quarters = 9, int perQuarter = 60, bool net = true, string? config = null, string variant = "e2.0/L5", string formation = "aaaaaaaaaaaaaaaa")
    {
        var rng = new SeededRandom(seed);
        for (var q = 0; q < quarters; q++)
        {
            var start = new DateOnly(2023, 1, 1).AddMonths(3 * q);
            for (var i = 0; i < perQuarter; i++)
            {
                var r = mean + PairsData.Normal(rng);
                var date = start.AddDays(i);
                var epoch = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset).ToUnixTimeSeconds();
                var netInr = (decimal)Math.Round(r * 600.0, 2);
                _store.Stored.Add(new PairStoredTrade("run" + q, "NSE:Y" + (i % 10).ToString(CultureInfo.InvariantCulture) + "|NSE:X", date, epoch, r, netInr + 30m, net ? netInr : null, ["variant:" + variant, "cfg:" + (config ?? Config), "quarter:" + PairQuarter.Label(start), "formation:" + formation, "turnover_inr:200000.00"]));
            }
        }
    }

    [Fact]
    public async Task A_primary_variant_that_is_not_in_the_ledger_is_refused_before_any_trade_is_read()
    {
        await LedgerAsync(50, includePrimary: false);
        Trades(1, 0.6);

        var result = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.trial_not_in_ledger");
        _store.ReadTradesCalls.ShouldBe(0);
        _evaluations.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_verdict_uses_the_ledger_count_and_stores_a_protocol_S_record_with_the_quarter_fold_unit_and_the_primary_trial()
    {
        await LedgerAsync(2006);
        Trades(2, 1.0);
        _store.Stats = [Stored("2024Q1", "aaaaaaaaaaaaaaaa", 0, new PairFormationStat("2024Q1", 100, 98, 2, 3, [2.0, 5.0, 9.0])), Stored("2024Q2", "aaaaaaaaaaaaaaaa", 1, new PairFormationStat("2024Q2", 100, 100, 0, 0, []))];

        var result = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var record = _evaluations.Records.ShouldHaveSingleItem();
        record.Family.ShouldBe("pairs:intraday-1");
        record.Protocol.ShouldBe("S");
        record.FoldUnit.ShouldBe("quarter");
        record.VersionKey.ShouldBe("pairs-1");
        record.Trials.ShouldBe(2006);
        record.Factor.ShouldBe(MultipleTesting.Factor(2006), 1e-12);
        record.TrialHash.ShouldBe(PairTrial.Variant(PairVariant.Primary, PairStudyOptions.Default, DateOnly.MinValue, DateOnly.MinValue).Hash);
        record.Verdict.ShouldBe("KEEP", string.Join("; ", record.Missing));
        record.HoldOutRead.ShouldBeFalse();
        using var inputs = JsonDocument.Parse(record.InputsJson);
        var sources = inputs.RootElement.GetProperty("sources");
        sources.GetProperty("pair_walker").GetString().ShouldBe("pair-1");
        sources.GetProperty("fill_engine").GetString().ShouldBe("sim-9");
        sources.GetProperty("charge_basis").GetString().ShouldBe("gross");
        result.Value.Note.ShouldNotBeNull().ShouldContain("ledger: N 2006, F 3.74");
        result.Value.Note.ShouldContain("half-life (sessions): n 3, min 2.0, median 5.0, max 9.0");
        result.Value.Note.ShouldContain("without a selected pair 50 %");
        result.Value.Note.ShouldContain("turnover per trade: 200000 INR");
        result.Value.Note.ShouldContain("e2.0/L5 n=");
    }

    [Fact]
    public async Task Fewer_than_six_quarters_is_NOT_EVALUABLE_naming_the_missing_quantities_and_the_evidence_has_no_digit_for_a_non_value()
    {
        await LedgerAsync(2006);
        Trades(3, 0.6, quarters: 3);

        await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        var record = _evaluations.Records.Single();
        record.Verdict.ShouldBe("NOT_EVALUABLE");
        record.Missing.ShouldContain(m => m.StartsWith("quarters ", StringComparison.Ordinal) && m.EndsWith(" of 6", StringComparison.Ordinal));
        record.EstimatesJson.ShouldContain("\"day_lower\":\"unavailable\"");
        record.Limits.ShouldContain(l => l.StartsWith("ban_list: unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_re_run_stores_the_same_record_id_and_writes_nothing_new()
    {
        await LedgerAsync(2006);
        Trades(4, 1.0);
        var appended = _ledger.Appended;

        var first = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);
        var second = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        first.Value.Records.Single().Stored.ShouldBeTrue();
        second.Value.Records.Single().Stored.ShouldBeFalse();
        second.Value.Records.Single().EvaluationId.ShouldBe(first.Value.Records.Single().EvaluationId);
        _evaluations.Records.Count.ShouldBe(1);
        _ledger.Appended.ShouldBe(appended);
    }

    [Fact]
    public async Task A_gross_result_that_would_be_a_keep_is_inconclusive()
    {
        await LedgerAsync(2006);
        Trades(5, 0.6, net: false);

        await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        _evaluations.Records.Single().Verdict.ShouldBe("INCONCLUSIVE");
    }

    [Fact]
    public async Task Trades_of_other_settings_and_other_variants_are_not_judged_and_the_others_are_counted_in_the_grid()
    {
        await LedgerAsync(2006);
        Trades(6, 0.6, quarters: 3);
        Trades(7, 0.6, config: "deadbeef");
        Trades(8, 0.2, quarters: 2, variant: "e1.5/L5");

        var result = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        var record = _evaluations.Records.Single();
        record.Missing.ShouldContain(m => m.StartsWith("quarters ", StringComparison.Ordinal) && m.EndsWith(" of 6", StringComparison.Ordinal));
        result.Value.Note.ShouldNotBeNull().ShouldContain("59 stored trades of other settings"); // only those dated before the hold-out
        using var evidence = JsonDocument.Parse(record.EvidenceJson);
        var grid = evidence.RootElement.GetProperty("variant_grid").EnumerateArray().ToDictionary(g => g.GetProperty("variant").GetString()!, g => g.GetProperty("trades").GetInt32());
        grid["e1.5/L5"].ShouldBe(59); // development trades only: the hold-out is not in the grid
        grid["e2.0/L5"].ShouldBe(59);
        grid["e2.5/L10"].ShouldBe(0);
    }

    [Fact]
    public async Task The_hold_out_is_read_once_after_a_development_keep_and_a_second_reading_is_refused_before_anything_is_read()
    {
        await LedgerAsync(2006);
        Trades(9, 1.0);

        var first = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, true), TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Records.Count.ShouldBe(2);
        first.Value.Records[0].HoldOutRead.ShouldBeFalse();
        first.Value.Records[1].HoldOutRead.ShouldBeTrue();
        first.Value.Records[1].Verdict.ShouldBe("KEEP");
        var reads = _store.ReadTradesCalls;

        var second = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, true), TestContext.Current.CancellationToken);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("extensions.holdout_used");
        _store.ReadTradesCalls.ShouldBe(reads);
        _evaluations.Records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Without_the_flag_the_hold_out_is_not_read_and_after_a_development_that_is_not_a_keep_it_is_not_read_either()
    {
        await LedgerAsync(2006);
        Trades(10, 0.0);

        var plain = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);
        var flagged = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, true), TestContext.Current.CancellationToken);

        plain.Value.Records.Single().HoldOutRead.ShouldBeFalse();
        flagged.Value.Records.Single().HoldOutRead.ShouldBeFalse();
        flagged.Value.Note.ShouldNotBeNull().ShouldContain("The hold-out was not read");
        _evaluations.Records.ShouldNotContain(r => r.HoldOutRead);
    }

    [Fact]
    public async Task An_unknown_family_is_refused()
    {
        var result = await Handler().HandleAsync(new EvaluatePairsCommand("pead", false), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
        _store.ReadTradesCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Only_the_hold_out_trades_differ_and_the_development_record_and_the_note_are_byte_identical()
    {
        await LedgerAsync(2006);
        Trades(11, 1.0);
        var plain = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);
        var firstRecord = _evaluations.Records.Single();

        var changed = new EvaluatePairsHandlerTests();
        await changed.LedgerAsync(2006);
        changed.Trades(11, 1.0);
        var holdStart = new DateOnly(2024, 9, 2);
        for (var i = 0; i < changed._store.Stored.Count; i++)
        {
            var t = changed._store.Stored[i];
            if (t.Date >= holdStart)
            {
                changed._store.Stored[i] = t with { R = 55, GrossInr = 99_999m, NetInr = null, Labels = [.. t.Labels.Select(l => l.StartsWith("turnover_inr:", StringComparison.Ordinal) ? "turnover_inr:9.00" : l)] };
            }
        }

        var other = await changed.Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        var secondRecord = changed._evaluations.Records.Single();
        secondRecord.EvaluationId.ShouldBe(firstRecord.EvaluationId);
        secondRecord.EvidenceJson.ShouldBe(firstRecord.EvidenceJson);
        secondRecord.EstimatesJson.ShouldBe(firstRecord.EstimatesJson);
        secondRecord.InputsJson.ShouldBe(firstRecord.InputsJson);
        other.Value.Note.ShouldBe(plain.Value.Note);
    }

    [Fact]
    public async Task Of_two_formations_of_a_quarter_the_latest_is_judged_and_the_trades_of_the_other_are_counted_apart()
    {
        await LedgerAsync(2006);
        Trades(12, 1.0, formation: "aaaaaaaaaaaaaaaa");
        Trades(13, 1.0, formation: "bbbbbbbbbbbbbbbb");
        _store.Stats = [Stored("2024Q1", "aaaaaaaaaaaaaaaa", 0, new PairFormationStat("2024Q1", 10, 10, 0, 1, [3.0])), Stored("2024Q1", "bbbbbbbbbbbbbbbb", 5, new PairFormationStat("2024Q1", 10, 10, 0, 2, [4.0, 6.0]))];

        var result = await Handler().HandleAsync(new EvaluatePairsCommand(PairFamilies.Intraday, false), TestContext.Current.CancellationToken);

        var record = _evaluations.Records.Single();
        record.Rows.ShouldBe(415);
        result.Value.Note.ShouldNotBeNull().ShouldContain("formations 1: selected per formation [2024Q1=2]");
        result.Value.Note.ShouldContain("420 stored trades of other settings");
    }
}
