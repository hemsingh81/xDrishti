#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using static XDrishti.Application.Tests.Learning.MlInfluenceKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>A model registry in memory with the rules of the real one: a role is the latest designation, naming a champion retires the previous one, and each designation writes its changelog row (the previous champion is found in it).</summary>
internal sealed class FakeModelRegistry(ChangelogHandlersTests.FakeChangelog log) : IModelRegistry
{
    public Dictionary<long, StoredModel> Models { get; } = [];

    public List<(long Id, ModelRole Role, string Actor, string Reason, IReadOnlyList<string> Evidence)> Designations { get; } = [];

    public ValueTask<RegisteredModel> RegisterAsync(ModelToRegister model, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<int> CountOtherModelsAsync(string featureVersion, string sha256, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<StoredModel?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Models.GetValueOrDefault(id));

    public ValueTask<IReadOnlyList<StoredModel>> ListAsync(ModelRole? role, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredModel>>([.. Models.Values.Where(m => role is null || m.Role == role).OrderByDescending(m => m.Id).Take(limit)]);

    public ValueTask<IReadOnlyList<StoredDesignation>> DesignationsAsync(long id, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredDesignation>>([.. Designations.Where(d => d.Id == id).Select((d, i) => new StoredDesignation(i + 1, d.Id, d.Role, d.Reason, DateTimeOffset.UnixEpoch, d.Actor))]);

    /// <summary>Runs inside <see cref="DesignateAsync"/> as the write starts (before the role is read under the lock), to let another caller change the role first.</summary>
    public Action<long>? BeforeDesignate { get; set; }

    public ValueTask<bool> DesignateAsync(long id, ModelRole role, string reason, string actor, DateTimeOffset at, IReadOnlyList<string> evidence, ModelRole? expectedCurrent, CancellationToken cancellationToken)
    {
        BeforeDesignate?.Invoke(id);
        var model = Models[id];
        if (model.Role == role || (expectedCurrent is { } expected && model.Role != expected))
        {
            return ValueTask.FromResult(false);
        }

        long? replaced = null;
        if (role == ModelRole.Champion)
        {
            foreach (var old in Models.Values.Where(m => m.Role == ModelRole.Champion && m.Id != id).ToList())
            {
                Models[old.Id] = old with { Role = ModelRole.Retired };
                replaced ??= old.Id;
                log.Add(new NewChangelogEntry("designation", "model", $"model:{old.Id}", "champion", "retired", $"replaced by model {id}", actor, [$"model:{old.Id}", .. evidence], $"model:{old.Id}", $"model:{id}", null, null, at));
            }
        }

        log.Add(new NewChangelogEntry("designation", "model", $"model:{id}", model.Role.Code(), role.Code(), reason, actor, [$"model:{id}", .. evidence], replaced is { } r ? $"model:{r}" : null, $"model:{id}", null, null, at));
        Models[id] = model with { Role = role };
        Designations.Add((id, role, actor, reason, evidence));
        return ValueTask.FromResult(true);
    }
}

/// <summary>An in-memory rollback store with the rules of the table: one proposal per model, trigger and day; one move from proposed; and the guard trigger (an automatic decision needs the newest ml.auto_rollback row to be ON and recorded, and never dismisses).</summary>
internal sealed class FakeRollbackStore(Func<MlSwitches> switches) : IRollbackProposalReader, IRollbackProposalStore
{
    public List<RollbackProposalRow> Rows { get; } = [];

    /// <summary>Called inside <see cref="DecideAsync"/> before the compare-and-set, to let another caller win first.</summary>
    public Action<long>? BeforeDecide { get; set; }

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UnixEpoch;

    public ValueTask<long?> InsertAsync(NewRollbackProposal proposal, DateOnly day, CancellationToken cancellationToken)
    {
        if (Rows.Any(r => r.Proposal.ModelId == proposal.ModelId && r.Proposal.Trigger == proposal.Trigger && r.ProposedOn == day))
        {
            return ValueTask.FromResult<long?>(null);
        }

        var id = Rows.Count + 1L;
        Rows.Add(new RollbackProposalRow(id, proposal, day, Clock(), "proposed", null, null, null));
        return ValueTask.FromResult<long?>(id);
    }

    public ValueTask<RollbackDecideResult> DecideAsync(long id, string state, string actor, long? autoSettingId, CancellationToken cancellationToken)
    {
        BeforeDecide?.Invoke(id);
        var index = Rows.FindIndex(r => r.Id == id && r.State == "proposed");
        if (index < 0)
        {
            return ValueTask.FromResult(RollbackDecideResult.NotProposed);
        }

        var current = switches().AutoRollback;
        if (actor == MlActors.Auto && (state != "applied" || !current.On || autoSettingId != current.Id))
        {
            return ValueTask.FromResult(RollbackDecideResult.Refused);
        }

        Rows[index] = Rows[index] with { State = state, Actor = actor, AutoSettingId = autoSettingId, DecidedAt = Clock() };
        return ValueTask.FromResult(RollbackDecideResult.Decided);
    }

    public ValueTask<RollbackProposalRow?> FindAsync(long id, CancellationToken cancellationToken) => ValueTask.FromResult(Rows.FirstOrDefault(r => r.Id == id));

    public ValueTask<IReadOnlyList<RollbackProposalRow>> ListAsync(string? state, long? modelId, int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<RollbackProposalRow>>([.. Rows.Where(r => (state is null || r.State == state) && (modelId is null || r.Proposal.ModelId == modelId)).OrderByDescending(r => r.Id).Take(limit)]);
}

/// <summary>The real rollback, promotion, switch and daily-run handlers over in-memory stores. The switches, the evidence and the models are fields the test sets.</summary>
internal sealed class MlRollbackFixture
{
    public MlRollbackFixture()
    {
        Registry = new FakeModelRegistry(Log);
        Store = new FakeRollbackStore(() => Switches) { Clock = () => Time.GetUtcNow() };
        Settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Switches));
        Checks.ExistsAsync(Arg.Any<long>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(CheckExists));
        Checks.InsertAsync(Arg.Any<NewMlMonitorCheck>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(true));
        Evidence.ReadAsync(Arg.Any<StoredModel>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Inputs));
        Challenger.ReadAsync(Arg.Any<StoredModel>(), Arg.Any<StoredModel?>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Figures));
        Predicate.ReadAsync(Arg.Any<StoredModel>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(PredicateEvidence));
        Influence.IsOnAsync(Arg.Any<CancellationToken>()).Returns(false);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public ChangelogHandlersTests.FakeChangelog Log { get; } = new();

    public FakeModelRegistry Registry { get; }

    public FakeRollbackStore Store { get; }

    public FakeHoldOutStore HoldOuts { get; } = new(Block());

    public IMlSwitchSettings Settings { get; } = Substitute.For<IMlSwitchSettings>();

    public IMlMonitorChecks Checks { get; } = Substitute.For<IMlMonitorChecks>();

    public IRollbackEvidence Evidence { get; } = Substitute.For<IRollbackEvidence>();

    public IChallengerEvidence Challenger { get; } = Substitute.For<IChallengerEvidence>();

    public IMlInfluenceEvidence Predicate { get; } = Substitute.For<IMlInfluenceEvidence>();

    public IMlInfluenceSwitch Influence { get; } = Substitute.For<IMlInfluenceSwitch>();

    public MlSwitches Switches { get; set; } = MlSwitches.AllOff;

    public bool CheckExists { get; set; }

    public RollbackInputs Inputs { get; set; } = RollbackInputs.None;

    public ChallengerFigures Figures { get; set; } = ChallengerFigures.None;

    public MlInfluenceEvidence PredicateEvidence { get; set; } = MlInfluenceEvidence.None;

    public MlInfluenceOptions Options { get; } = new();

    public MlInfluenceEvaluator Evaluator => new(Registry, new ModelHoldOutLooks(HoldOuts, Time), Predicate, Influence, Options);

    public DesignateModelHandler Designate => new(Registry, Time, NullLogger<DesignateModelHandler>.Instance);

    public DemoteModelHandler Demote => new(Designate);

    public ApplyRollbackHandler Apply => new(Store, Store, Settings, Registry, Demote, NullLogger<ApplyRollbackHandler>.Instance);

    public ProposeRollbackHandler Propose => new(Registry, Evidence, Store, Store, Checks, Settings, Log, Evaluator, Apply, Options, Time, NullLogger<ProposeRollbackHandler>.Instance);

    public PromoteChallengerHandler Promote => new(Registry, Challenger, Evaluator, Settings, Store, Designate, Options, NullLogger<PromoteChallengerHandler>.Instance);

    public EvaluateMlModelsHandler Run => new(Registry, Settings, Propose, Promote, NullLogger<EvaluateMlModelsHandler>.Instance);

    /// <summary>Adds a registered model (an eligible one by default, as <see cref="MlInfluenceKit.Model"/> builds it).</summary>
    public StoredModel Add(long id, ModelRole role = ModelRole.Challenger, char hash = 'e')
    {
        var model = Model(id, role, hash: hash);
        Registry.Models[id] = model;
        return model;
    }

    /// <summary>Gives a model the evidence the predicate needs (without the switch) and its one look at the hold-out, so that the predicate holds for it except for the owner's switch.</summary>
    public async Task MakePredicateHoldAsync(StoredModel model)
    {
        (await new ModelHoldOutLooks(HoldOuts, Time).ClaimAsync(model, "owner", "owner", new string('d', 64), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        PredicateEvidence = new MlInfluenceEvidence(new MlHoldOutFigures(150, 0.97, 0.05), new MlForwardFacts(40, 6, 0.1, 3), false);
    }

    public static MlSwitches On(bool influence = false, bool promote = false, bool rollback = false) =>
        new(new MlSwitchState(influence ? 3 : null, influence), new MlSwitchState(promote ? 4 : null, promote), new MlSwitchState(rollback ? 5 : null, rollback));

    /// <summary>200 scored outcomes whose predictions (0.7) are 20 pp above the observed rate (0.5): on the P6-09 defaults (window 100, every 50, 3 checks, 5 pp and 2.5 standard errors) the calibration detector fires at the third check.</summary>
    public static IReadOnlyList<CalibrationObservation> MiscalibratedOutcomes(int count = 200, double predicted = 0.7) =>
        [.. Enumerable.Range(0, count).Select(i => new CalibrationObservation("o" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i, predicted, i % 2 == 0 ? 1 : 0))];

    public static RollbackInputs FiringCalibration() => new(MiscalibratedOutcomes(), null);

    public async Task<ProposeRollbackOutcome> ProposeAsync(long modelId)
    {
        var result = await Propose.HandleAsync(new ProposeRollbackCommand(modelId), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }
}
