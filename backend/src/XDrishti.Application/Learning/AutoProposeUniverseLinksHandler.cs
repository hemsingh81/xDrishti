using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The auto option of the tier proposer (BK-11b, ADR 0266): after <c>xd learn cells --by-universe</c> has stored a snapshot, each observation that has no hypothesis yet is proposed as the actor <c>auto</c> when the setting <c>learning.universe_link.auto_propose</c> is ON (it is OFF
/// by default: then nothing is proposed). The rationale records the setting change (its id and hash) so that an auto proposal is never mistaken for the owner's. An observation whose hold-out is not stable (insufficient or unavailable inputs) is never an observation, so it is never proposed. Nothing is applied and no experiment starts.
/// </summary>
/// <param name="Sources">The runs the cells were made from: the baseline of a hypothesis is the run of the cell's strategy.</param>
public sealed record AutoProposeUniverseLinksCommand(IReadOnlyList<CellSource> Sources) : ICommand;

/// <summary>What the auto proposer did.</summary>
/// <param name="Enabled">The setting was ON.</param>
/// <param name="Proposed">Hypotheses written (actor <c>auto</c>).</param>
/// <param name="AlreadyProposed">Observations that already have a hypothesis (open or closed), left alone.</param>
/// <param name="NoBaseline">Observations of a strategy none of the given runs belongs to.</param>
/// <param name="LimitReached">The open limit stopped the proposing.</param>
public sealed record AutoProposeOutcome(bool Enabled, int Proposed, int AlreadyProposed, int NoBaseline, bool LimitReached);

internal sealed class AutoProposeUniverseLinksHandler(ICellStatsStore cells, IUniverseSettingsStore settings, IHypothesisLog log, IRunStore runs, UniverseLinkProposer proposer, ILogger<AutoProposeUniverseLinksHandler> logger)
    : ICommandHandler<AutoProposeUniverseLinksCommand, AutoProposeOutcome>
{
    /// <summary>The actor of an automatic proposal; never an owner.</summary>
    public const string AutoActor = "auto";

    private const int ScanLimit = 1000;

    public async ValueTask<Result<AutoProposeOutcome>> HandleAsync(AutoProposeUniverseLinksCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var state = await settings.CurrentAsync(cancellationToken);
        if (!state.Settings.AutoPropose)
        {
            return Result.Success(new AutoProposeOutcome(false, 0, 0, 0, false));
        }

        var change = state.History.FirstOrDefault(h => string.Equals(h.Key, UniverseSettingsRules.AutoProposeKey, StringComparison.Ordinal));
        var record = string.Create(CultureInfo.InvariantCulture, $"auto: {UniverseSettingsRules.AutoProposeKey} ON (change {(change is null ? "-" : change.Id.ToString(CultureInfo.InvariantCulture))}, hash {(change is null ? "-" : change.Hash[..Math.Min(12, change.Hash.Length)])})");
        var baselines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in command.Sources)
        {
            if (await runs.FindAsync(source.RunKey, cancellationToken) is { } header)
            {
                baselines.TryAdd(header.StrategyId, source.RunKey);
            }
        }

        var known = await log.ListAsync(null, ScanLimit, cancellationToken);
        var links = await UniverseLinks.BuildAsync(cells, settings, null, cancellationToken);
        int proposed = 0, already = 0, noBaseline = 0;
        var limit = false;
        foreach (var link in links.Where(l => l.Observation).OrderBy(l => l.ChildKey, StringComparer.Ordinal).ThenBy(l => l.Window == "all" ? 0 : 1).DistinctBy(l => l.ChildKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (known.Any(h => string.Equals(h.Row.Source, "universe", StringComparison.Ordinal) && string.Equals(h.Row.CellKey, link.ParentKey, StringComparison.Ordinal) && string.Equals(h.Row.CandidateValue, link.Universe, StringComparison.Ordinal)))
            {
                already++;
                continue;
            }

            var strategy = CellId.TryParse(link.ChildKey)?.Strategy;
            if (strategy is null || !baselines.TryGetValue(strategy, out var baseline))
            {
                noBaseline++;
                continue;
            }

            var result = await proposer.ProposeAsync(link, baseline, record, AutoActor, record, cancellationToken);
            if (result.IsSuccess)
            {
                proposed++;
            }
            else if (result.Error.Code == LearningErrors.HypothesisExistsCode)
            {
                already++;
            }
            else if (result.Error.Code == HypothesisErrors.OpenLimitCode)
            {
                limit = true;
                break;
            }
        }

        UniverseProposerLog.AutoProposed(logger, proposed, AutoActor);
        return Result.Success(new AutoProposeOutcome(true, proposed, already, noBaseline, limit));
    }
}

internal static partial class UniverseProposerLog
{
    [LoggerMessage(EventId = 4597, Level = LogLevel.Information, Message = "learning.universe_link.proposed {Count} hypotheses as {ActorKind}")]
    public static partial void AutoProposed(ILogger logger, int count, string actorKind);
}
