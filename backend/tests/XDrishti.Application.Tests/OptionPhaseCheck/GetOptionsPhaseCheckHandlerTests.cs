#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.OptionPhaseCheck;
using XDrishti.Application.OptionReports;
using XDrishti.Application.Tests.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Application.Tests.OptionPhaseCheck;

/// <summary>Spec P14-17 criteria 1 to 4 and 6 on the use case: it reads the stored records, resolves the versions each rests on, judges them through the pure rule and writes nothing. A source that cannot be read is an open family or an unavailable figure, never an error of the call or a MET.</summary>
public sealed class GetOptionsPhaseCheckHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 6, 0, 0, TimeSpan.Zero);

    private static readonly string Run = new('1', 64);

    private static readonly string Cash = new('2', 64);

    private static readonly Guid Nifty = Guid.Parse("11111111-1111-1111-1111-111111111111");


    private sealed class FakePhase : IOptionsPhaseReader
    {
        public Dictionary<string, PhaseRunInputs> Runs { get; } = [];

        public Dictionary<long, DateTimeOffset> Ended { get; } = [];

        public DateTimeOffset? Latest { get; set; } = Now;

        public List<NonCashCellRow> Rows { get; } = [];

        public bool Recorder { get; set; }

        public bool Backfill { get; set; }

        public bool CellsThrow { get; set; }

        public List<string> Asked { get; } = [];

        public ValueTask<IReadOnlyList<PhaseRunInputs>> RunInputsAsync(IReadOnlyCollection<string> runIds, CancellationToken cancellationToken)
        {
            Asked.Add("runs " + runIds.Count);
            return ValueTask.FromResult<IReadOnlyList<PhaseRunInputs>>([.. runIds.Where(Runs.ContainsKey).Select(id => Runs[id])]);
        }

        public ValueTask<IReadOnlyDictionary<long, DateTimeOffset>> FetchEndedAtAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<long, DateTimeOffset>>(Ended.Where(e => ids.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value));

        public ValueTask<DateTimeOffset?> LatestFetchEndedAtAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Latest);

        public ValueTask<bool> IsChainRecorderOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Recorder);

        public ValueTask<bool> IsBackfillOnAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Backfill);

        public ValueTask<IReadOnlyList<NonCashCellRow>> NonCashCellRowsAsync(int limit, CancellationToken cancellationToken)
        {
            Asked.Add("cells limit " + limit);
            return CellsThrow ? throw new InvalidOperationException("no table") : ValueTask.FromResult<IReadOnlyList<NonCashCellRow>>([.. Rows.Take(limit)]);
        }
    }

    private sealed class Throwing : IExtensionEvaluationReader
    {
        public ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken) => throw new InvalidOperationException("relation does not exist");
    }

    private sealed class Rig
    {
        public FakeEvaluationReader Evaluations { get; } = new();

        public FakePhase Phase { get; } = new();

        public FakeReportReader Runs { get; } = new();

        public Dictionary<string, string> Sheets { get; } = [];

        public Dictionary<string, PhaseCashRun> CashRuns { get; } = [];

        public Dictionary<Guid, string> Models { get; } = [];

        public IExtensionEvaluationReader Reader { get; set; }

        public Rig()
        {
            Reader = Evaluations;
        }

        public GetOptionsPhaseCheckHandler Handler()
        {
            var archive = Substitute.For<IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>>>();
            archive.HandleAsync(Arg.Any<GetOptionArchiveSummaryQuery>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new OptionArchiveSummaryView(
            [
                new UnderlyingArchiveSummary("NIFTY", ReportedCount.Of(12), ReportedCount.Of(12), ReportedDate.None("x"), ReportedDate.None("x"), new GapsView("unavailable", "x", []), new BackfillView("ok", null, 40, 0, 0, 2), ReportedCount.None("no_model"), ReportedDate.None("no_model"), ReportedDate.None("no_model")),
            ]))));
            var bars = Substitute.For<IBarQueries>();
            bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BarTotals(3, 100, Now.AddDays(-767), Now, 0, 0)));
            var engine = Substitute.For<ISimulationEngineVersion>();
            engine.EngineVersion.Returns("sim-9");
            var sheets = Substitute.For<ICostSheetHashes>();
            sheets.RatesHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(Sheets.GetValueOrDefault(call.Arg<string>())));
            var cashRuns = Substitute.For<ICashRunHeaders>();
            cashRuns.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(CashRuns.GetValueOrDefault(call.Arg<string>())));
            var models = Substitute.For<ISpreadModelIds>();
            models.LatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(Models.GetValueOrDefault(call.Arg<Guid>())));
            var remark = Substitute.For<IRemarkSettings>();
            remark.CondorCostSheetId.Returns("sheet-1");
            var settings = Substitute.For<IOptionsPhaseCheckSettings>();
            settings.StaleAfterDays.Returns(90);
            return new GetOptionsPhaseCheckHandler(Reader, Phase, Runs, archive, bars, engine, sheets, cashRuns, models, remark, settings, new FakeTimeProvider(Now), NullLogger<GetOptionsPhaseCheckHandler>.Instance);
        }

        public async Task<OptionsPhaseReport> RunAsync(params string[] cut)
        {
            var result = await Handler().HandleAsync(new OptionsPhaseCheckQuery(cut), TestContext.Current.CancellationToken);
            return result.Value.Report;
        }
    }

    private static string Inputs(string family, string runId, string cashRun, bool primary = true, string basis = "net", string? profile = null, string? developmentRecord = null) =>
        "{\"sources\":{\"expression_run\":\"" + runId + "\",\"cash_run\":\"" + cashRun + "\",\"profile\":\"" + (profile ?? ExpressionProfiles.PrimaryHashOf(family)) + "\",\"primary_profile\":\"" + (primary ? "true" : "false") + "\",\"charge_basis\":\"" + basis + "\",\"development_record\":\"" + (developmentRecord ?? "none") + "\"}}";

    private static ExtensionEvaluationRecord Record(string family, string verdict, string inputs, bool holdOut = false, string id = "", IReadOnlyList<string>? missing = null, int rows = 500) =>
        new(string.IsNullOrEmpty(id) ? family + verdict + holdOut : id, family, ExpressionFamilies.ProtocolOf(family), null, "expr-1", "none", new string('a', 64), null, null, 4, 1.4, "quarter", 6, 6, rows, null, "{}", verdict, missing ?? [], [], "{}", inputs, holdOut);

    private static string RunInputs(long watermark = 7, string sheet = "sha-now", string model = "model-now") =>
        "{\"format\":\"expr-remark/1\",\"archive_watermark\":" + watermark + ",\"cost_sheet\":\"" + sheet + "\",\"spread\":[\"" + Nifty.ToString("N") + ":" + model + ":none\"]}";

    private static void CurrentRun(Rig rig, string family, string remarkVersion = "expr-1", string engineVersion = "sim-9", string sheetInRun = "sha-now", string sheetNow = "sha-now", string modelInRun = "model-now", long watermark = 7)
    {
        rig.Phase.Runs[Run] = new PhaseRunInputs(Run, remarkVersion, RunInputs(watermark, sheetInRun, modelInRun));
        rig.CashRuns[Cash] = new PhaseCashRun(Cash, engineVersion, "sheet-1");
        rig.Phase.Ended[7] = Now.AddDays(-10);
        rig.Models[Nifty] = "model-now";
        rig.Sheets["sheet-1"] = sheetNow;
        rig.Runs.Runs.Add(new ExpressionRunStored(Run, Cash, ExpressionProfiles.PrimaryHashOf(family)!, remarkVersion, Now, 100, 80, "{}", 0.04, [], 20, 5, new Dictionary<string, int>()));
    }

    [Fact]
    public async Task An_unknown_family_in_cut_is_refused_naming_the_known_ones_and_nothing_is_read()
    {
        var rig = new Rig();

        var result = await rig.Handler().HandleAsync(new OptionsPhaseCheckQuery(["expr:bogus"]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("options.unknown_family");
        result.Error.Message.ShouldContain("expr:bogus");
        result.Error.Message.ShouldContain("pairs:intraday-1");
        rig.Evaluations.Asked.ShouldBeEmpty();
        rig.Phase.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_stored_is_not_evaluable_and_the_archive_figures_are_read()
    {
        var rig = new Rig();

        var report = await rig.RunAsync();

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Summary.ShouldBe("no P14 evaluation stored.");
        report.YearsHeld.ShouldNotBeNull().ShouldBe(2.1, 0.01);
        report.Underlyings.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new OptionsUnderlyingFact("NIFTY", 12, 40, 2));
        report.RecorderOn.ShouldBe(false);
        report.Families.Count.ShouldBe(OptionsPhaseCheck.Families.Count);
    }

    [Fact]
    public async Task A_current_net_drop_per_expression_family_is_met_as_stays_disabled_and_the_shares_come_from_the_run()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        foreach (var family in ExpressionFamilies.All)
        {
            CurrentRun(rig, family);
            rig.Evaluations.Records.Add(Record(family, "DROP", Inputs(family, Run, Cash)));
        }

        rig.Evaluations.Records.Add(Record(IvFeatureCatalog.Family, "DROP", "{}") with { Protocol = "X", ExtensionVersion = "iv-1", VersionKey = ExtensionReport.VersionKeyOf(ExtensionCatalogs.GroupOf(IvFeatureCatalog.Version)!) });
        rig.Evaluations.Records.Add(Record(PairFamilies.Intraday, "DROP", """{"sources":{"charge_basis":"gross","fill_engine":"sim-9"}}""") with { Protocol = "S", VersionKey = "pairs-1" });
        rig.Phase.Recorder = true;

        var report = await rig.RunAsync();

        report.Verdict.ShouldBe(PhaseVerdicts.Met, string.Join(" | ", report.Open));
        report.ArchiveNote.ShouldNotBeNull().ShouldContain("archive still recording after DROP");
        var line = report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);
        (line.Resolved, line.Basis, line.DerivedExpiryShare, line.FallbackShare).ShouldBe((true, "net", 0.25, 0.04));
    }

    [Fact]
    public async Task A_secondary_profile_record_never_decides_and_the_primary_one_does()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "KEEP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash, primary: false), id: "secondary"));
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "DROP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash), id: "primary"));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        (line.RecordId, line.Status, line.Resolved).ShouldBe(("primary", "DROP", true));
    }

    [Theory]
    [InlineData("remark_version", "expr-0")]
    [InlineData("engine", "sim-8")]
    [InlineData("cost_sheet", "other")]
    [InlineData("spread_model", "model-old")]
    [InlineData("profile", "x")]
    public async Task A_record_whose_re_mark_engine_sheet_spread_model_or_profile_is_not_current_is_stale(string what, string value)
    {
        var rig = new Rig();
        CurrentRun(
            rig,
            ExpressionFamilies.OptionBuy,
            remarkVersion: what == "remark_version" ? value : "expr-1",
            engineVersion: what == "engine" ? value : "sim-9",
            sheetInRun: what == "cost_sheet" ? value : "sha-now",
            modelInRun: what == "spread_model" ? value : "model-now");
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "KEEP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash, profile: what == "profile" ? value : null)));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        line.Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        line.Detail.ShouldContain(what);
        line.Resolved.ShouldBeFalse();
    }

    [Fact]
    public async Task An_archive_watermark_older_than_the_setting_is_stale()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Phase.Ended[7] = Now.AddDays(-120);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "DROP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash)));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        line.Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        line.Detail.ShouldContain("more than 90 days behind the newest fetch");
    }

    [Fact]
    public async Task A_stopped_archive_does_not_age_a_verdict_because_the_bound_is_against_the_newest_fetch()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Phase.Ended[7] = Now.AddDays(-300);
        rig.Phase.Latest = Now.AddDays(-290);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "DROP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash)));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        (line.Status, line.Resolved).ShouldBe(("DROP", true));
    }

    [Fact]
    public async Task The_iv_family_compares_its_archive_watermark_and_a_stored_net_decides_its_costs_word()
    {
        var rig = new Rig();
        rig.Phase.Ended[9] = Now.AddDays(-200);
        var record = Record(IvFeatureCatalog.Family, "DROP", "{\"sources\":{\"archive_watermark\":\"9\"}}") with { Protocol = "X", ExtensionVersion = "iv-1", VersionKey = ExtensionReport.VersionKeyOf(ExtensionCatalogs.GroupOf(IvFeatureCatalog.Version)!) };
        rig.Evaluations.Records.Add(record);

        var stale = (await rig.RunAsync()).Families.Single(f => f.Family == IvFeatureCatalog.Family);
        rig.Phase.Ended[9] = Now.AddDays(-10);
        var current = (await rig.RunAsync()).Families.Single(f => f.Family == IvFeatureCatalog.Family);

        stale.Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        (current.Status, current.Resolved, current.Basis).ShouldBe(("DROP", true, "unavailable (protocol X may be gross)"));
        rig.Evaluations.Records[0] = record with { InputsJson = "{\"sources\":{\"archive_watermark\":\"0\",\"charge_basis\":\"net\"}}" };
        (await rig.RunAsync()).Families.Single(f => f.Family == IvFeatureCatalog.Family).Basis.ShouldBe("net");
    }

    [Fact]
    public async Task A_credit_gate_record_is_not_evaluable_with_its_missing_quantity_not_stale()
    {
        var rig = new Rig();
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.CreditSpread, "NOT_EVALUABLE", Inputs(ExpressionFamilies.CreditSpread, "none", Cash), missing: ["no stored regime"], rows: 0));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.CreditSpread);

        (line.Status, line.Resolved).ShouldBe(("NOT_EVALUABLE", false));
        line.Detail.ShouldContain("no stored regime");
    }

    [Fact]
    public async Task A_missing_re_mark_run_makes_the_record_stale_never_current()
    {
        var rig = new Rig();
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "DROP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash)));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        line.Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        line.Resolved.ShouldBeFalse();
    }

    [Fact]
    public async Task A_development_keep_without_a_hold_out_record_is_open_and_with_a_confirming_one_is_resolved()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "KEEP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash), id: "dev"));

        var open = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);
        rig.Evaluations.Records.Insert(0, Record(ExpressionFamilies.OptionBuy, "KEEP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash, developmentRecord: "dev"), holdOut: true, id: "hold"));
        var closed = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        (open.Resolved, open.Detail).ShouldBe((false, "hold-out not read: a development KEEP is a candidate, not evidence"));
        (closed.Resolved, closed.HoldOut).ShouldBe((true, "read"));
    }

    [Fact]
    public async Task A_gross_record_leaves_the_family_open()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "INCONCLUSIVE", Inputs(ExpressionFamilies.OptionBuy, Run, Cash, basis: "gross"), missing: ["costs are gross"]));

        var line = (await rig.RunAsync()).Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);

        (line.Status, line.Resolved, line.Basis).ShouldBe(("INCONCLUSIVE", false, "gross"));
        line.Detail.ShouldContain("costs are gross");
    }

    [Fact]
    public async Task A_stored_plan_item_with_a_non_cash_key_is_not_met_with_its_row_id_and_the_read_is_bounded()
    {
        var rig = new Rig();
        rig.Phase.Rows.Add(new NonCashCellRow("trading.plan_items", 4711));

        var report = await rig.RunAsync();

        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        report.Violations.ShouldHaveSingleItem().Id.ShouldBe(4711);
        rig.Phase.Asked.ShouldContain($"cells limit {OptionsPhaseCheck.MaxViolationRows + 1}");
    }

    [Fact]
    public async Task Unreadable_stores_make_families_open_and_figures_unavailable_never_an_error_or_a_met()
    {
        var rig = new Rig { Reader = new Throwing() };
        rig.Phase.CellsThrow = true;

        var report = await rig.RunAsync();

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Families.ShouldAllBe(f => f.Status == OptionsPhaseCheck.StatusUnreadable);
        report.CellsReadable.ShouldBeFalse();
    }

    [Fact]
    public async Task The_dropped_basis_family_is_refused_in_cut_as_unknown()
    {
        var rig = new Rig();

        var result = await rig.Handler().HandleAsync(new OptionsPhaseCheckQuery(["basis:carry-1"]), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("options.unknown_family");
    }

    [Fact]
    public async Task A_cut_family_is_listed_and_not_read()
    {
        var rig = new Rig();

        var report = await rig.RunAsync(ExpressionFamilies.IronCondor, IvFeatureCatalog.Family);

        rig.Evaluations.Asked.Select(a => a.Family).ShouldNotContain(ExpressionFamilies.IronCondor);
        rig.Evaluations.Asked.Select(a => a.Family).ShouldNotContain(IvFeatureCatalog.Family);
        report.Cut.ShouldBe([ExpressionFamilies.IronCondor, IvFeatureCatalog.Family]);
    }

    [Fact]
    public async Task The_same_stored_rows_give_identical_output()
    {
        var rig = new Rig();
        CurrentRun(rig, ExpressionFamilies.OptionBuy);
        rig.Evaluations.Records.Add(Record(ExpressionFamilies.OptionBuy, "DROP", Inputs(ExpressionFamilies.OptionBuy, Run, Cash)));

        var first = await rig.Handler().HandleAsync(new OptionsPhaseCheckQuery([]), TestContext.Current.CancellationToken);
        var second = await rig.Handler().HandleAsync(new OptionsPhaseCheckQuery([]), TestContext.Current.CancellationToken);

        (first.Value.Text, first.Value.Json).ShouldBe((second.Value.Text, second.Value.Json));
        first.Value.Text.ShouldContain("limits:");
    }

    [Fact]
    public async Task Every_read_of_the_evaluation_store_is_bounded_by_a_limit()
    {
        var rig = new Rig();

        await rig.RunAsync();

        rig.Evaluations.Asked.Count.ShouldBe(OptionsPhaseCheck.Families.Count);
        rig.Evaluations.Asked.ShouldAllBe(a => a.Limit == 100);
    }
}
