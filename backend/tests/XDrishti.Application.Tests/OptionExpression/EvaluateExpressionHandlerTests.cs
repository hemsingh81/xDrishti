using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.OptionExpression;
using XDrishti.Application.Tests.Pairs;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>
/// P14-09 criteria 3, 4 and 7 at the use case: the trial is appended to the ledger before any date, count or figure of the run is read; a trial the ledger does not hold is refused with nothing computed; a re-run stores the same id and appends nothing; without the flag no hold-out
/// window is requested, with it one extra record, a second reading refused before anything is read; the hold-out trades do not change the development record; honest states and the shared record.
/// </summary>
public sealed class EvaluateExpressionHandlerTests
{
    private static readonly string RunKey = new('a', 64);
    private static readonly DateOnly First = new(2023, 1, 2);

    private readonly Journal _journal = new();
    private readonly FakeLedger _ledger;
    private readonly FakeEvaluations _evaluations = new();
    private readonly FakeReader _reader;
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IExpressionProfileStore _profiles = Substitute.For<IExpressionProfileStore>();
    private readonly IOptionReference _reference = Substitute.For<IOptionReference>();
    private readonly IRegimeLabels _regime = Substitute.For<IRegimeLabels>();
    private readonly ISpreadModelStore _spreadModels = Substitute.For<ISpreadModelStore>();
    private readonly ISpreadModelSettings _spreadSettings = Substitute.For<ISpreadModelSettings>();
    private readonly string _primary = ExpressionProfiles.PrimaryHash;

    public EvaluateExpressionHandlerTests()
    {
        _ledger = new FakeLedger(_journal);
        _reader = new FakeReader(_journal);
        _runs.FindAsync(RunKey, Arg.Any<CancellationToken>()).Returns(Header("lab:full"));
        _profiles.GetAsync(_primary, Arg.Any<CancellationToken>()).Returns(Stored(_primary));
        _reference.EpochsAsync(Arg.Any<CancellationToken>()).Returns([new MarketEpoch("nifty_expiry_tuesday", new DateOnly(2025, 1, 1), "x", null, true)]);
    }

    private static StoredRunHeader Header(string createdBy) =>
        new(1, RunKey, "sim", 5, 5, "ORB", new string('c', 64), First, new DateOnly(2025, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, createdBy);

    private static StoredExpressionProfile Stored(string hash) => new(Profile(hash), DateTimeOffset.UnixEpoch);

    private static ExpressionProfile Profile(string hash) =>
        hash == ExpressionProfiles.PrimaryHash ? XDrishti.Domain.Options.Remark.OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value : XDrishti.Domain.Options.Remark.OptionProfileParams.Create("itm1", "nearest_safe", null, 1).Value.ToProfile().Value;

    private EvaluateExpressionHandler Handler()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return new EvaluateExpressionHandler(_runs, _profiles, _reader, _reference, _regime, _spreadModels, _spreadSettings, _ledger, _evaluations, new EvaluationWriter(_evaluations, _ledger, time), NullLogger<EvaluateExpressionHandler>.Instance);
    }

    private static EvaluateExpressionCommand Command(bool holdOut = false, string? profile = null) => new("expr:option_buy", RunKey, profile, holdOut);

    /// <summary>Twelve quarters from 2023: the first three are in sample, the nine after them are test windows, the last two of those are the hold-out.</summary>
    private void Rows(ulong seed, double meanD, bool net = true, Func<DateOnly, bool>? tweak = null)
    {
        var rng = new SeededRandom(seed);
        for (var q = 0; q < 12; q++)
        {
            var qs = First.AddMonths(3 * q);
            qs = new DateOnly(qs.Year, qs.Month, 1);
            for (var i = 0; i < 90; i++)
            {
                var date = qs.AddDays(i % 80);
                var cash = 0.05 + (0.8 * PairsData.Normal(rng));
                var d = meanD + (0.5 * PairsData.Normal(rng));
                if (tweak?.Invoke(date) == true)
                {
                    d = 77;
                }

                var entry = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset).AddSeconds(i);
                _reader.Pairs.Add(new ExpressionPairRow("T" + q + "-" + i, entry, entry.AddMinutes(40), cash + d, cash, net, "contract", i % 2 == 0 ? "sizable" : "not_sizable", []));
            }
        }
    }

    private async Task<int> LedgerCountAsync() => await _ledger.CountVariantsAsync("expr:option_buy", CancellationToken.None);

    [Fact]
    public async Task The_trial_is_appended_to_the_ledger_before_any_date_count_or_figure_of_the_run_is_read()
    {
        Rows(1, 0.4);

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var events = _journal.Events;
        events.IndexOf("ledger").ShouldBe(0);
        events.IndexOf("dates").ShouldBeGreaterThan(events.IndexOf("ledger"));
        events.ShouldContain("paired");
        events.ShouldContain("unpaired");
        events.Where(e => e is "dates" or "paired" or "unpaired").ShouldAllBe(e => events.IndexOf(e) > events.IndexOf("ledger"));
        _ledger.Rows.ShouldHaveSingleItem().StrategyId.ShouldBe("expr:option_buy");
        _ledger.Rows[0].RunId.ShouldBe(ExpressionTrial.Of("expr:option_buy", _primary, RunKey, First, new DateOnly(2025, 12, 31)).RunId);
    }

    [Fact]
    public async Task A_trial_the_ledger_does_not_hold_is_refused_with_nothing_computed()
    {
        Rows(2, 0.4);
        _ledger.DropAppends = true;

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.trial_not_in_ledger");
        _reader.Reads.ShouldBe(0);
        _evaluations.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_planted_net_effect_is_stored_as_a_protocol_O_quarter_record_with_the_trial_and_the_sources()
    {
        Rows(3, 0.4);

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var record = _evaluations.Records.ShouldHaveSingleItem();
        record.Family.ShouldBe("expr:option_buy");
        (record.Protocol, record.FoldUnit, record.VersionKey, record.Trials).ShouldBe(("O", "quarter", "expr-1", 1));
        record.TrialHash.ShouldBe(ExpressionTrial.Of("expr:option_buy", _primary, RunKey, First, new DateOnly(2025, 12, 31)).Hash);
        record.Verdict.ShouldBe("KEEP", string.Join("; ", record.Missing) + " " + string.Join(" | ", JsonDocument.Parse(record.EvidenceJson).RootElement.GetProperty("failed").EnumerateArray().Select(e => e.GetString())));
        record.HoldOutRead.ShouldBeFalse();
        using var inputs = JsonDocument.Parse(record.InputsJson);
        var sources = inputs.RootElement.GetProperty("sources");
        sources.GetProperty("cash_run").GetString().ShouldBe(RunKey);
        sources.GetProperty("profile").GetString().ShouldBe(_primary);
        sources.GetProperty("primary_profile").GetString().ShouldBe("true");
        sources.GetProperty("charge_basis").GetString().ShouldBe("net");
        result.Value.Note.ShouldNotBeNull().ShouldContain("ledger: N 1, F 1.00");
        result.Value.Note.ShouldContain("(primary)");
    }

    [Fact]
    public async Task A_secondary_profile_is_another_counted_trial_and_N_and_the_margin_grow()
    {
        Rows(4, 0.4);
        var second = Profile("secondary").Hash;
        _profiles.GetAsync(second, Arg.Any<CancellationToken>()).Returns(Stored(second));

        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var result = await Handler().HandleAsync(Command(profile: second), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _evaluations.Records.Count.ShouldBe(2);
        _evaluations.Records[1].Trials.ShouldBe(2);
        _evaluations.Records[1].Factor.ShouldBe(MultipleTesting.Factor(2), 1e-12);
        (await LedgerCountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_re_run_stores_the_same_record_id_appends_nothing_to_the_ledger_and_keeps_N()
    {
        Rows(5, 0.4);

        var first = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var rows = _ledger.Appended;
        var second = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        first.Value.Records.Single().Stored.ShouldBeTrue();
        second.Value.Records.Single().Stored.ShouldBeFalse();
        second.Value.Records.Single().EvaluationId.ShouldBe(first.Value.Records.Single().EvaluationId);
        _evaluations.Records.Count.ShouldBe(1);
        _ledger.Appended.ShouldBe(rows);
    }

    [Fact]
    public async Task The_same_rows_in_another_order_give_the_same_record_id()
    {
        Rows(6, 0.4);
        var first = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        _reader.Pairs.Reverse();

        var second = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        second.Value.Records.Single().EvaluationId.ShouldBe(first.Value.Records.Single().EvaluationId);
    }

    [Fact]
    public async Task Without_the_flag_no_hold_out_window_is_requested_and_the_hold_out_trades_do_not_change_the_development_record()
    {
        Rows(7, 0.4);
        var plain = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);
        var holdStart = new DateOnly(2025, 12, 19).AddMonths(-6).AddDays(1);
        _reader.Requested.Where(w => w.Count > 0).ShouldAllBe(w => w.All(x => x.End < holdStart));
        var changed = new EvaluateExpressionHandlerTests();
        changed.Rows(7, 0.4, tweak: d => d >= holdStart);
        // the planted hold-out values differ: the pairs dated from the start have d = 77, the development ones are untouched
        var other = await changed.Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        other.Value.Records.Single().EvaluationId.ShouldBe(plain.Value.Records.Single().EvaluationId);
        changed._evaluations.Records.Single().EvidenceJson.ShouldBe(_evaluations.Records.Single().EvidenceJson);
        other.Value.Note.ShouldBe(plain.Value.Note);
    }

    [Fact]
    public async Task The_hold_out_is_read_once_after_a_development_keep_and_a_second_reading_is_refused_before_anything_is_read()
    {
        Rows(8, 0.4);

        var first = await Handler().HandleAsync(Command(holdOut: true), TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Records.Count.ShouldBe(2);
        first.Value.Records[0].HoldOutRead.ShouldBeFalse();
        first.Value.Records[1].HoldOutRead.ShouldBeTrue();
        first.Value.Records[1].Verdict.ShouldBe("KEEP");
        _reader.Requested.Any(w => w.Any(x => x.End >= new DateOnly(2025, 6, 20))).ShouldBeTrue();
        var events = _journal.Events.Count;

        var second = await Handler().HandleAsync(Command(holdOut: true), TestContext.Current.CancellationToken);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("extensions.holdout_used");
        _journal.Events.Count.ShouldBe(events);
        _evaluations.Records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_secondary_profile_cannot_read_the_hold_out_and_nothing_is_counted()
    {
        Rows(20, 0.4);
        var second = Profile("secondary").Hash;
        _profiles.GetAsync(second, Arg.Any<CancellationToken>()).Returns(Stored(second));

        var result = await Handler().HandleAsync(Command(holdOut: true, profile: second), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("expression.holdout_primary_only");
        _ledger.Appended.ShouldBe(0);
        _reader.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task The_hold_out_cut_does_not_depend_on_which_trades_were_paired()
    {
        _reader.Unpaired["no_series"] = 5; // closed trades exist (dates) but none is paired

        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        _reader.Requested.ShouldNotBeEmpty();
        _reader.Requested.ShouldAllBe(w => w.All(x => x.End < new DateOnly(2025, 6, 21)));
    }

    [Fact]
    public async Task After_a_development_that_is_not_a_keep_the_flag_does_not_read_the_hold_out()
    {
        Rows(9, 0.0);

        var result = await Handler().HandleAsync(Command(holdOut: true), TestContext.Current.CancellationToken);

        result.Value.Records.Single().HoldOutRead.ShouldBeFalse();
        result.Value.Note.ShouldNotBeNull().ShouldContain("The hold-out was not read");
        _reader.Requested.ShouldAllBe(w => w.All(x => x.End < new DateOnly(2025, 6, 20)));
    }

    [Fact]
    public async Task A_gross_run_is_inconclusive_and_names_the_basis()
    {
        Rows(10, 0.4, net: false);

        await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        var record = _evaluations.Records.Single();
        record.Verdict.ShouldBe("INCONCLUSIVE");
        using var inputs = JsonDocument.Parse(record.InputsJson);
        inputs.RootElement.GetProperty("sources").GetProperty("charge_basis").GetString().ShouldBe("gross");
    }

    [Fact]
    public async Task A_run_with_no_paired_trade_is_NOT_EVALUABLE_naming_each_missing_quantity_and_prints_no_digit_for_a_non_value()
    {
        _reader.Unpaired["spread_unavailable"] = 40;

        var result = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var record = _evaluations.Records.Single();
        record.Verdict.ShouldBe("NOT_EVALUABLE");
        record.Missing.ShouldContain(m => m.StartsWith("no_paired_trades", StringComparison.Ordinal));
        record.Missing.ShouldContain(m => m.StartsWith("spread_unavailable", StringComparison.Ordinal));
        record.EstimatesJson.ShouldContain("\"mean_d\":\"unavailable\"");
        record.EvidenceJson.ShouldContain("\"basis\":\"unavailable\"");
    }

    [Fact]
    public async Task An_unknown_run_profile_family_or_a_run_not_re_marked_or_not_from_a_full_test_is_refused_before_the_ledger()
    {
        Rows(11, 0.4);
        _runs.FindAsync(new string('f', 64), Arg.Any<CancellationToken>()).Returns((StoredRunHeader?)null);
        _runs.FindAsync(new string('9', 64), Arg.Any<CancellationToken>()).Returns(Header("simulate"));

        var family = await Handler().HandleAsync(Command() with { Family = "pead" }, TestContext.Current.CancellationToken);
        var profile = await Handler().HandleAsync(Command(profile: new string('1', 64)), TestContext.Current.CancellationToken);
        var run = await Handler().HandleAsync(Command() with { RunKey = new string('f', 64) }, TestContext.Current.CancellationToken);
        var notFull = await Handler().HandleAsync(Command() with { RunKey = new string('9', 64) }, TestContext.Current.CancellationToken);
        _reader.RemarkMissing = true;
        var remark = await Handler().HandleAsync(Command(), TestContext.Current.CancellationToken);

        family.Error.Code.ShouldBe("extensions.invalid_query");
        profile.Error.Code.ShouldBe("expression.profile_not_found");
        run.Error.Code.ShouldBe("expression.run_not_found");
        notFull.Error.Code.ShouldBe("expression.run_not_full");
        remark.Error.Code.ShouldBe("expression.remark_not_found");
        _ledger.Appended.ShouldBe(0);
        _evaluations.Records.ShouldBeEmpty();
    }

    private sealed class FakeReader(Journal journal) : IExpressionReader
    {
        public List<ExpressionPairRow> Pairs { get; } = [];

        public Dictionary<string, int> Unpaired { get; } = [];

        public List<IReadOnlyList<ExpressionWindow>> Requested { get; } = [];

        public bool RemarkMissing { get; set; }

        public int Reads { get; private set; }

        public ValueTask<ExpressionRunRef?> FindRunAsync(string cashRunKey, string profileHash, CancellationToken cancellationToken) =>
            ValueTask.FromResult(RemarkMissing ? null : new ExpressionRunRef(new string('7', 64), DateTimeOffset.UnixEpoch));

        public ValueTask<ExpressionDates> DatesAsync(string runId, CancellationToken cancellationToken)
        {
            journal.Events.Add("dates");
            Reads++;
            if (Pairs.Count == 0)
            {
                return ValueTask.FromResult(new ExpressionDates(Unpaired.Count == 0 ? [] : [new SideBounds("CE", First, new DateOnly(2025, 12, 20))], null));
            }

            var dates = Pairs.Select(p => IndiaTime.Today(p.EntryTs)).ToList();
            return ValueTask.FromResult(new ExpressionDates([new SideBounds("CE", dates.Min(), dates.Max())], dates.Max()));
        }

        public ValueTask<IReadOnlyList<ExpressionPairRow>> PairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
        {
            journal.Events.Add("paired");
            Reads++;
            Requested.Add(windows);
            return ValueTask.FromResult<IReadOnlyList<ExpressionPairRow>>([.. Pairs.Where(p => windows.Any(w => w.Side == "CE" && IndiaTime.Today(p.EntryTs) >= w.Start && IndiaTime.Today(p.EntryTs) <= w.End))]);
        }

        public ValueTask<IReadOnlyDictionary<string, int>> UnpairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
        {
            journal.Events.Add("unpaired");
            Reads++;
            Requested.Add(windows);
            return ValueTask.FromResult<IReadOnlyDictionary<string, int>>(windows.Count == 0 ? new Dictionary<string, int>() : new Dictionary<string, int>(Unpaired));
        }
    }
}
