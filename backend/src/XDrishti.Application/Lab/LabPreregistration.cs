using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The pre-registration side of a Lab post (spec CF-06, D1 to D3): the registration check, the extra exit looks of a hash's first full test and the freeze of the plan with its re-plan row. Used by the post of a run; nothing here executes a run.
/// </summary>
internal sealed class LabPreregistration(
    IRegistrationStore registrations,
    IPlanFreezeStore freezes,
    ITestLedger ledger,
    ICostSheetStore sheets,
    IStrategyChecker checker,
    LabSettings settings,
    RegistrationOptions options)
{
    /// <summary>The slippage preset until the tiers are wired (CF-05b).</summary>
    public const string SlippageModelled = "modelled";

    /// <summary>The refusal of an unregistered full or robust run when enforcement is on; otherwise null (the report carries the warning).</summary>
    public async ValueTask<Error?> CheckAsync(StoredVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        return options.Enforce && await registrations.GetAsync(version.StrategyId, version.Version, cancellationToken) is null
            ? LabErrors.NotRegistered(version.StrategyId, version.Version)
            : null;
    }

    /// <summary>The extra exit looks this post adds to the ledger: one less than the candidates when the hash has no full or robust run yet (D3), else 0.</summary>
    public async ValueTask<int> ExtraTrialsAsync(string strategyId, string strategyHash, CancellationToken cancellationToken)
    {
        // shortcut: the check of the first full run and the append are two statements, so two first full posts of one hash at the same moment would both add the extra exit looks (an over-count, which only makes the luck bar stricter). Upgrade: decide it inside the ledger insert.
        var exits = ExitFlags.Parse([.. settings.Exits], null, []);
        return exits is null || await ledger.HasFullRunAsync(strategyId, strategyHash, cancellationToken)
            ? 0
            : TrialCounter.ExtraExits(exits.Candidates.Count);
    }

    /// <summary>The hash of the plan this run would execute now (D2): the strategy hash, the universe, the window, the costs, the sweep plan, the candidate exits, the slippage and the registration text. The freeze stores it and the hold-out pass recomputes it.</summary>
    public async ValueTask<string> PlanHashAsync(LabRun run, StoredVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(version);
        var registration = await registrations.GetAsync(run.StrategyId, run.Version, cancellationToken);
        var sheet = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
        var costs = sheet.IsSuccess ? $"{sheet.Value.Id}|{sheet.Value.RatesSha256}|checked={(sheet.Value.Checked is not null ? "yes" : "no")}" : "none";
        return PlanHash.Of(new PlanInput(
            run.StrategyHash,
            run.Universe.Label,
            LabRunIdentity.Sha256(string.Join(",", run.Universe.Instruments.Select(i => i.Id.ToString("D", CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal))),
            run.From,
            run.To,
            costs,
            Sweeps(version),
            [.. settings.Exits],
            SlippageModelled,
            registration?.TextHash ?? "none"));
    }

    /// <summary>Freezes the plan of the run's version; a plan that differs from one it already froze appends a <c>replan</c> row to the ledger (D2). Idempotent per run.</summary>
    public async ValueTask FreezeAsync(LabRun run, StoredVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(version);
        var hash = await PlanHashAsync(run, version, cancellationToken);
        var frozen = await freezes.FreezeAsync(run.StrategyId, run.Version, hash, run.Id, run.CreatedAt, cancellationToken);
        if (frozen is { Inserted: true, PriorFreezes: > 0 })
        {
            var id = new Guid(Convert.FromHexString(LabRunIdentity.Sha256("replan|" + run.Id.ToString("N", CultureInfo.InvariantCulture) + "|" + hash)[..32]));
            await ledger.AppendAsync(new LedgerRow(id, run.Key, run.StrategyId, run.Version, run.StrategyHash, version.ParentHash, run.Universe.Label, run.From, run.To, run.Method, run.CreatedAt, "replan"), cancellationToken);
        }
    }

    /// <summary>The sweep plan of the version: the declared parameters the Lab would sweep and the values it tries. A text that does not compile has none (the run fails at its compile stage).</summary>
    private IReadOnlyList<PlanSweep> Sweeps(StoredVersion version)
    {
        var described = checker.Check(version.SourceText).Description;
        return described is null
            ? []
            : [.. SweepPlan.Build(described.Parameters, settings.Robustness.SweepParams, settings.Robustness.SweepSteps).GroupBy(p => p.Parameter).Select(g => new PlanSweep(g.Key, [.. g.Select(p => p.Value)]))];
    }
}
