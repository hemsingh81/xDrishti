#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Application.Tests.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>An in-memory kill store with the rules of the table: one proposal per strategy, rule and day, and one move from proposed.</summary>
internal sealed class FakeKillStore : IKillProposalReader, IKillProposalStore
{
    public List<KillProposalRow> Rows { get; } = [];

    /// <summary>Called inside <see cref="DecideAsync"/> before the compare-and-set, to let another caller win first.</summary>
    public Action<long>? BeforeDecide { get; set; }

    public int Decisions { get; private set; }

    /// <summary>The time a decision is stamped with (the database clock in production).</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UnixEpoch;

    public ValueTask<long?> InsertAsync(NewKillProposal proposal, DateOnly day, CancellationToken cancellationToken)
    {
        if (Rows.Any(r => r.Proposal.Strategy == proposal.Strategy && r.Proposal.Rule == proposal.Rule && r.ProposedOn == day))
        {
            return ValueTask.FromResult<long?>(null);
        }

        var id = Rows.Count + 1L;
        Rows.Add(new KillProposalRow(id, proposal, day, DateTimeOffset.UnixEpoch, "proposed", null, null, null));
        return ValueTask.FromResult<long?>(id);
    }

    /// <summary>When set, the record refuses every decision (as the database does when the switch is not ON now).</summary>
    public bool Refuse { get; set; }

    public ValueTask<KillDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken)
    {
        BeforeDecide?.Invoke(id);
        var index = Rows.FindIndex(r => r.Id == id && r.State == "proposed");
        if (index < 0)
        {
            return ValueTask.FromResult(KillDecideResult.NotProposed);
        }

        if (Refuse)
        {
            return ValueTask.FromResult(KillDecideResult.Refused);
        }

        Decisions++;
        Rows[index] = Rows[index] with { State = state, Actor = actor, AutoSettingId = autoSettingId, DecidedAt = Clock() };
        return ValueTask.FromResult(KillDecideResult.Decided);
    }

    public ValueTask<KillProposalRow?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

    public ValueTask<IReadOnlyList<KillProposalRow>> ListAsync(string? state, string? strategy, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<KillProposalRow>>([.. Rows.Where(r => (state is null || r.State == state) && (strategy is null || r.Proposal.Strategy == strategy)).OrderByDescending(r => r.Id).Take(limit)]);

    public ValueTask<KillProposalRow?> LatestAsync(string strategy, string rule, long? epochId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rows.Where(r => r.Proposal.Strategy == strategy && r.Proposal.Rule == rule && r.Proposal.EpochId == epochId).OrderByDescending(r => r.Id).FirstOrDefault());
}

/// <summary>The real pause, re-enable, apply and propose handlers over an in-memory changelog, an in-memory store and a scripted decay report. The switch is a field the test sets.</summary>
internal sealed class KillFixture
{
    public const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";
    public const string OtherCell = "ORB|15m|SELL|intraday|FIXED_RR|*";

    public KillFixture()
    {
        Store.Clock = () => Time.GetUtcNow();
        Settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Switch));
        Checks.ExistsAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(CheckExists));
        Checks.InsertAsync(Arg.Any<NewDecayCheck>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(true));
        Report.HandleAsync(Arg.Any<GetDecayReportQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(ReportResult));
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public ChangelogHandlersTests.FakeChangelog Log { get; } = new();

    public FakeKillStore Store { get; } = new();

    public IKillSettings Settings { get; } = Substitute.For<IKillSettings>();

    public IDecayCheckStore Checks { get; } = Substitute.For<IDecayCheckStore>();

    public IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> Report { get; } = Substitute.For<IQueryHandler<GetDecayReportQuery, Result<DecayReportView>>>();

    public KillSwitch Switch { get; set; } = new(null, false);

    public bool CheckExists { get; set; }

    public Result<DecayReportView> ReportResult { get; set; } = Result.Success(View());

    public RecordCellTransitionHandler Record => new(Log, Time, NullLogger<RecordCellTransitionHandler>.Instance);

    public PauseCellHandler Pause => new(Record);

    public StrategyCells Cells => new(Log);

    public ApplyKillHandler Apply => new(Store, Store, Settings, Cells, Pause, NullLogger<ApplyKillHandler>.Instance);

    public ProposeKillHandler Propose => new(Report, Store, Store, Checks, Settings, Cells, Apply, new DecayOptions(), new KillOptions(), Time, NullLogger<ProposeKillHandler>.Instance);

    public ReEnableCellHandler ReEnable => new(Log, Store, Report, Record, NullLogger<ReEnableCellHandler>.Instance);

    public static DecayKillView Kill(string rule, string state, bool proposalOnly = false) => new(rule, state, -0.56, 0, 100, proposalOnly, $"the {rule} numbers");

    public static DecayReportView View(params DecayKillView[] kill) => new(
        "ORB", 1, true, new DecaySettingsView(60, 1.5, -0.05, new string('a', 64), "s", 0.3, true), new DecayReferenceView("available", null, 40, 0.1, 1, "net"),
        Series(), Series(), kill, [], "line");

    private static DecaySeriesView Series() => new("basis", null, "alert", 130, 10, 2, true, 2, [], null, [], null);

    /// <summary>Walks a cell to Paper (or further) through the real lifecycle with a claimed owner.</summary>
    public async Task WalkAsync(string cell, params CellState[] states)
    {
        foreach (var state in states)
        {
            var moved = await Record.HandleAsync(new RecordCellTransitionCommand(cell, state, "setup", "test", new TransitionFacts(true, true, true, true, 20), ["run:x"]), TestContext.Current.CancellationToken);
            moved.IsSuccess.ShouldBeTrue(moved.IsFailure ? moved.Error.Message : string.Empty);
        }
    }

    public async Task<ProposeKillOutcome> ProposeAsync(string strategy = "ORB")
    {
        var result = await Propose.HandleAsync(new ProposeKillCommand(strategy), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    public CellState StateOf(string cell) => CellHistory.Replay([.. Log.Rows.Where(r => r.Entry.Subject == cell)]).State;
}
