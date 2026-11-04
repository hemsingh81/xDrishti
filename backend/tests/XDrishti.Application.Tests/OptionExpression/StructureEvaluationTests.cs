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
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>
/// P14-10 criteria 3, 4 and 6 at the use case: each structure is its own family with its own trial hash and N; credit spreads and condors are NOT_EVALUABLE <c>regime_unavailable</c> or <c>spread_not_measured</c> without a trial counted and without a date or figure read; a profile of another kind is
/// refused; the condor is judged by protocol R on its population key with no cash run; the debit spread is judged by protocol O like the option.
/// </summary>
public sealed class StructureEvaluationTests
{
    private static readonly string RunKey = new('a', 64);
    private static readonly string Population = new('c', 64);
    private static readonly DateOnly First = new(2023, 1, 2);
    private static readonly DateOnly Last = new(2025, 12, 31);

    private readonly Journal _journal = new();
    private readonly FakeLedger _ledger;
    private readonly FakeEvaluations _evaluations = new();
    private readonly Reader _reader;
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IExpressionProfileStore _profiles = Substitute.For<IExpressionProfileStore>();
    private readonly IOptionReference _reference = Substitute.For<IOptionReference>();
    private readonly IRegimeLabels _regime = Substitute.For<IRegimeLabels>();
    private readonly ISpreadModelStore _spreadModels = Substitute.For<ISpreadModelStore>();
    private readonly ISpreadModelSettings _spreadSettings = Substitute.For<ISpreadModelSettings>();

    public StructureEvaluationTests()
    {
        _ledger = new FakeLedger(_journal);
        _reader = new Reader(_journal);
        _runs.FindAsync(RunKey, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, RunKey, "sim", 5, 5, "ORB", new string('c', 64), First, Last, "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "lab:full"));
        _reference.EpochsAsync(Arg.Any<CancellationToken>()).Returns([new MarketEpoch("nifty_expiry_tuesday", new DateOnly(2025, 1, 1), "x", null, true)]);
        _reference.FindUnderlyingAsync("NSE:NIFTY", Arg.Any<CancellationToken>()).Returns(Guid.Parse("00000000-0000-7000-8000-0000000000a1"));
        _spreadSettings.MinUsableSessions.Returns(60);
        _spreadModels.LatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HalfSpreadModel?)null);
        foreach (var family in ExpressionFamilies.All)
        {
            var hash = ExpressionProfiles.PrimaryHashOf(family)!;
            _profiles.GetAsync(hash, Arg.Any<CancellationToken>()).Returns(new StoredExpressionProfile(ProfileOf(family), DateTimeOffset.UnixEpoch));
        }
    }

    private static ExpressionProfile ProfileOf(string family) => family switch
    {
        ExpressionFamilies.DebitSpread => SpreadParams.Create(Expression.DebitSpread, "atm", 2, "nearest_safe", null, 1).Value.ToProfile().Value,
        ExpressionFamilies.CreditSpread => SpreadParams.Create(Expression.CreditSpread, "atm", 2, "nearest_safe", null, 1).Value.ToProfile().Value,
        ExpressionFamilies.IronCondor => CondorParams.Primary.ToProfile().Value,
        _ => XDrishti.Domain.Options.Remark.OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value,
    };

    private EvaluateExpressionHandler Handler()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return new EvaluateExpressionHandler(_runs, _profiles, _reader, _reference, _regime, _spreadModels, _spreadSettings, _ledger, _evaluations, new EvaluationWriter(_evaluations, _ledger, time), NullLogger<EvaluateExpressionHandler>.Instance);
    }

    private static EvaluateExpressionCommand Command(string family, string? run = null, bool holdOut = false, string? profile = null) => new(family, run ?? RunKey, profile, holdOut);

    private void Credit(bool regime, int? sessions)
    {
        _regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(regime);
        if (sessions is { } n)
        {
            _spreadModels.LatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new HalfSpreadModel("m", Guid.Empty, n, First, Last, []));
        }
    }

    /// <summary>Twelve quarters of rows: seven of them are test windows for a cash population, all development for the condor population.</summary>
    private void Rows(ulong seed, double meanD, bool condor = false)
    {
        var rng = new SeededRandom(seed);
        for (var q = 0; q < 12; q++)
        {
            var qs = new DateOnly(First.AddMonths(3 * q).Year, First.AddMonths(3 * q).Month, 1);
            for (var i = 0; i < (condor ? 40 : 90); i++)
            {
                var date = qs.AddDays(i % 80);
                var cash = condor ? 0 : 0.05 + (0.8 * PairsData.Normal(rng));
                var d = meanD + (0.5 * PairsData.Normal(rng));
                var entry = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset).AddSeconds(i);
                _reader.Pairs.Add(new ExpressionPairRow("T" + q + "-" + i, entry, entry.AddMinutes(40), cash + d, cash, true, "contract", "sizable", []));
            }
        }
    }

    [Fact]
    public async Task A_credit_spread_without_a_stored_regime_is_NOT_EVALUABLE_regime_unavailable_with_no_trial_counted_and_no_date_or_figure_read()
    {
        Credit(regime: false, sessions: 60);

        var result = await Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        var record = _evaluations.Records.ShouldHaveSingleItem();
        (record.Family, record.Protocol, record.Verdict).ShouldBe(("expr:credit_spread", "O", "NOT_EVALUABLE"));
        record.Missing.ShouldHaveSingleItem().ShouldStartWith("regime_unavailable");
        record.TrialHash.ShouldBeNull();
        _ledger.Appended.ShouldBe(0);
        _reader.Reads.ShouldBe(0);
        _journal.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_credit_spread_on_a_model_of_59_sessions_or_none_is_NOT_EVALUABLE_spread_not_measured_and_both_gates_are_named_together()
    {
        Credit(regime: true, sessions: 59);
        var short59 = await Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);

        var none = new StructureEvaluationTests();
        none.Credit(regime: false, sessions: null);
        var both = await none.Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);

        short59.IsSuccess.ShouldBeTrue();
        _evaluations.Records.ShouldHaveSingleItem().Missing.ShouldHaveSingleItem().ShouldContain("spread_not_measured (best measured model 59 of 60");
        _reader.Reads.ShouldBe(0);
        both.Value.Records.ShouldHaveSingleItem().Missing.Select(m => m.Split(' ')[0]).ShouldBe(["regime_unavailable", "spread_not_measured"]);
        none._ledger.Appended.ShouldBe(0);
    }

    [Fact]
    public async Task The_condor_gate_counts_no_trial_reads_no_run_and_stores_a_protocol_R_record_with_the_family_count()
    {
        Credit(regime: false, sessions: null);

        var result = await Handler().HandleAsync(Command(ExpressionFamilies.IronCondor, Population), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        var record = _evaluations.Records.ShouldHaveSingleItem();
        (record.Family, record.Protocol, record.FoldUnit, record.Verdict, record.Trials).ShouldBe(("expr:iron_condor", "R", "quarter", "NOT_EVALUABLE", 1));
        record.Missing.Count.ShouldBe(2);
        _ledger.Appended.ShouldBe(0);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, TestContext.Current.CancellationToken);
        _reader.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_re_run_of_a_failed_gate_stores_the_same_record_once()
    {
        Credit(regime: false, sessions: 60);

        var first = await Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);
        var second = await Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);

        _evaluations.Records.Count.ShouldBe(1);
        second.Value.Records.Single().EvaluationId.ShouldBe(first.Value.Records.Single().EvaluationId);
        second.Value.Records.Single().Stored.ShouldBeFalse();
    }

    [Fact]
    public async Task Each_structure_is_its_own_family_with_its_own_trial_hash_and_its_own_N()
    {
        Credit(regime: true, sessions: 60);
        Rows(1, 0.4);

        await Handler().HandleAsync(Command(ExpressionFamilies.OptionBuy), TestContext.Current.CancellationToken);
        var debit = await Handler().HandleAsync(Command(ExpressionFamilies.DebitSpread), TestContext.Current.CancellationToken);
        var credit = await Handler().HandleAsync(Command(ExpressionFamilies.CreditSpread), TestContext.Current.CancellationToken);

        debit.IsSuccess.ShouldBeTrue(debit.IsFailure ? debit.Error.Message : null);
        credit.IsSuccess.ShouldBeTrue(credit.IsFailure ? credit.Error.Message : null);
        _ledger.Rows.Select(r => r.StrategyId).ShouldBe(["expr:option_buy", "expr:debit_spread", "expr:credit_spread"]);
        _ledger.Rows.Select(r => r.StrategyHash).Distinct().Count().ShouldBe(3);
        _evaluations.Records.Select(r => (r.Family, r.Trials)).ShouldBe([("expr:option_buy", 1), ("expr:debit_spread", 1), ("expr:credit_spread", 1)]); // the trial of one family never counts in another
        _evaluations.Records.ShouldAllBe(r => r.Protocol == "O");
        _evaluations.Records[1].TrialHash.ShouldBe(ExpressionTrial.Of("expr:debit_spread", ExpressionProfiles.PrimaryHashOf("expr:debit_spread")!, RunKey, First, Last).Hash);
    }

    [Fact]
    public async Task A_profile_of_another_kind_is_refused_before_the_ledger_and_the_trial_is_in_the_ledger_before_any_read()
    {
        Credit(regime: true, sessions: 60);
        Rows(2, 0.4);
        var option = ProfileOf(ExpressionFamilies.OptionBuy);
        _profiles.GetAsync(option.Hash, Arg.Any<CancellationToken>()).Returns(new StoredExpressionProfile(option, DateTimeOffset.UnixEpoch));

        var mismatch = await Handler().HandleAsync(Command(ExpressionFamilies.DebitSpread, profile: option.Hash), TestContext.Current.CancellationToken);
        _journal.Events.ShouldBeEmpty();
        var ok = await Handler().HandleAsync(Command(ExpressionFamilies.DebitSpread), TestContext.Current.CancellationToken);

        mismatch.Error.Code.ShouldBe(EvaluateExpressionHandler.ProfileKindMismatchCode);
        ok.IsSuccess.ShouldBeTrue();
        _journal.Events.IndexOf("ledger").ShouldBe(0);
        _journal.Events.Where(e => e is "dates" or "paired" or "unpaired").ShouldAllBe(e => _journal.Events.IndexOf(e) > 0);
    }

    [Fact]
    public async Task The_condor_is_judged_by_protocol_R_on_its_population_with_no_cash_run_and_the_trial_in_the_ledger_first()
    {
        Credit(regime: true, sessions: 60);
        Rows(3, 0.4, condor: true);
        _reader.Run = new ExpressionRunRef(new string('7', 64), DateTimeOffset.UnixEpoch, First, Last);

        var result = await Handler().HandleAsync(Command(ExpressionFamilies.IronCondor, Population), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        var record = _evaluations.Records.ShouldHaveSingleItem();
        (record.Family, record.Protocol, record.FoldUnit, record.VersionKey).ShouldBe(("expr:iron_condor", "R", "quarter", "expr-1"));
        record.TrialHash.ShouldBe(ExpressionTrial.Of("expr:iron_condor", ExpressionProfiles.PrimaryHashOf("expr:iron_condor")!, Population, First, Last).Hash);
        using var inputs = JsonDocument.Parse(record.InputsJson);
        inputs.RootElement.GetProperty("sources").GetProperty("condor_population").GetString().ShouldBe(Population);
        using var evidence = JsonDocument.Parse(record.EvidenceJson);
        evidence.RootElement.GetProperty("protocol").GetString().ShouldBe("R");
        _journal.Events.IndexOf("ledger").ShouldBe(0);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, TestContext.Current.CancellationToken);
        result.Value.Note.ShouldNotBeNull().ShouldContain("condors");
    }

    [Fact]
    public async Task A_condor_population_that_was_not_simulated_is_refused_and_a_condor_run_without_dates_is_too()
    {
        Credit(regime: true, sessions: 60);
        _reader.Run = null;

        var missing = await Handler().HandleAsync(Command(ExpressionFamilies.IronCondor, Population), TestContext.Current.CancellationToken);
        _reader.Run = new ExpressionRunRef(new string('7', 64), DateTimeOffset.UnixEpoch);
        var undated = await Handler().HandleAsync(Command(ExpressionFamilies.IronCondor, Population), TestContext.Current.CancellationToken);

        missing.Error.Code.ShouldBe(EvaluateExpressionHandler.RemarkNotFoundCode);
        undated.Error.Code.ShouldBe(EvaluateExpressionHandler.RemarkNotFoundCode);
        _ledger.Appended.ShouldBe(0);
    }

    private sealed class Reader(Journal journal) : IExpressionReader
    {
        public List<ExpressionPairRow> Pairs { get; } = [];

        public ExpressionRunRef? Run { get; set; } = new(new string('7', 64), DateTimeOffset.UnixEpoch);

        public int Reads { get; private set; }

        public ValueTask<ExpressionRunRef?> FindRunAsync(string cashRunKey, string profileHash, CancellationToken cancellationToken) => ValueTask.FromResult(Run);

        public ValueTask<ExpressionDates> DatesAsync(string runId, CancellationToken cancellationToken)
        {
            journal.Events.Add("dates");
            Reads++;
            if (Pairs.Count == 0)
            {
                return ValueTask.FromResult(new ExpressionDates([], null));
            }

            var dates = Pairs.Select(p => IndiaTime.Today(p.EntryTs)).ToList();
            return ValueTask.FromResult(new ExpressionDates([new SideBounds("CE", dates.Min(), dates.Max()), new SideBounds("IC", dates.Min(), dates.Max())], dates.Max()));
        }

        public ValueTask<IReadOnlyList<ExpressionPairRow>> PairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
        {
            journal.Events.Add("paired");
            Reads++;
            return ValueTask.FromResult<IReadOnlyList<ExpressionPairRow>>([.. Pairs.Where(p => windows.Any(w => IndiaTime.Today(p.EntryTs) >= w.Start && IndiaTime.Today(p.EntryTs) <= w.End))]);
        }

        public ValueTask<IReadOnlyDictionary<string, int>> UnpairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken)
        {
            journal.Events.Add("unpaired");
            Reads++;
            return ValueTask.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        }
    }
}
