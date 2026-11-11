using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Proposes ONE hypothesis from a stored liquidity-tier observation (BK-11b, ADR 0266): knob <c>scan_scope</c>, from all shares to the tier, on the all-shares parent cell. The link must be an observation as the Setups view shows it (the tier is read in place of the parent, has at least
/// the link minimum of trades and a stable hold-out). Nothing is applied, no scan scope changes and no experiment starts.
/// </summary>
/// <param name="TierCellKey">The key of the tier cell (seven parts, universe <c>liq_high</c> or another tier).</param>
/// <param name="BaselineRunKey">The stored run the hypothesis is attached to (the one the cells were made from).</param>
/// <param name="Reason">The owner's reason, kept with the rationale.</param>
/// <param name="Actor">Who proposes: the owner, from the session or the command line.</param>
/// <param name="Window">The window code of the link (<c>all</c> when not given).</param>
public sealed record ProposeFromUniverseLinkCommand(string TierCellKey, string BaselineRunKey, string Reason, string Actor, string? Window = null) : ICommand;

internal sealed class ProposeFromUniverseLinkHandler(ICellStatsStore cells, IUniverseSettingsStore settings, UniverseLinkProposer proposer) : ICommandHandler<ProposeFromUniverseLinkCommand, HypothesisMoveOutcome>
{
    public async ValueTask<Result<HypothesisMoveOutcome>> HandleAsync(ProposeFromUniverseLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var window = string.IsNullOrWhiteSpace(command.Window) ? CellWindow.All.Code() : command.Window;
        if (CellId.TryParse(command.TierCellKey) is not { } tier || tier.Universe == CellId.AllUniverses || CellWindowExtensions.Parse(window) is null)
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.Invalid([new FieldError("tierCellKey", "Name a tier cell (seven parts, the last a liquidity tier) and a window of all, 250d or 60d.")]));
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 200 || command.Reason.Any(char.IsControl))
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.Invalid([new FieldError("reason", "The reason is required, at most 200 characters.")]));
        }

        var links = await UniverseLinks.BuildAsync(cells, settings, tier.Strategy, cancellationToken);
        var link = links.FirstOrDefault(l => string.Equals(l.ChildKey, command.TierCellKey, StringComparison.Ordinal) && string.Equals(l.Window, window, StringComparison.Ordinal));
        if (link is null)
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.CellNotFound(command.TierCellKey));
        }

        var state = await settings.CurrentAsync(cancellationToken);
        if (!link.Observation)
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.NotAnObservation(UniverseLinkProposal.NotObservationReason(link.Reason, link.ChildN, state.Settings.LinkMinTrades, link.HoldOut)));
        }

        return await proposer.ProposeAsync(link, command.BaselineRunKey, command.Reason.Trim(), command.Actor, null, cancellationToken);
    }
}

/// <summary>The one place a tier observation becomes a hypothesis, shared by the owner's act and the auto option so that both write the same row (BK-11b). Never applies anything.</summary>
internal sealed class UniverseLinkProposer(ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome> hypotheses, IHypothesisLog log)
{
    private const int ScanLimit = 1000;

    /// <summary>Writes the hypothesis, or refuses: a second open proposal of the same tier for the same cell is <c>learning.hypothesis_exists</c>; the open limit is the one of every proposal.</summary>
    /// <param name="link">An observation.</param>
    /// <param name="baselineRunKey">The stored baseline run.</param>
    /// <param name="reason">The reason of the owner, or the auto record (setting row id and hash).</param>
    /// <param name="actor">The owner or <c>auto</c>.</param>
    /// <param name="autoRecord">For the auto option: the setting and its change, kept in the rationale; null for the owner's act.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async ValueTask<Result<HypothesisMoveOutcome>> ProposeAsync(UniverseLinkView link, string baselineRunKey, string reason, string actor, string? autoRecord, CancellationToken cancellationToken)
    {
        var parent = CellId.TryParse(link.ParentKey);
        var child = CellId.TryParse(link.ChildKey);
        if (parent is null || child is null)
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.Invalid([new FieldError("tierCellKey", "The stored link names a cell that is not a valid key.")]));
        }

        var open = (await log.ListAsync(null, ScanLimit, cancellationToken))
            .FirstOrDefault(h => HypothesisStateText.TryParse(h.Row.State) is { } s && HypothesisLifecycle.IsOpen(s)
                && string.Equals(h.Row.Source, "universe", StringComparison.Ordinal) && string.Equals(h.Row.CellKey, link.ParentKey, StringComparison.Ordinal) && string.Equals(h.Row.CandidateValue, link.Universe, StringComparison.Ordinal));
        if (open is not null)
        {
            return Result.Failure<HypothesisMoveOutcome>(LearningErrors.HypothesisExists(open.Id));
        }

        var sentence = UniverseLinkProposal.Rationale(child.Strategy, child.Timeframe, link.Universe, link.ChildLower, link.ParentLower, link.ChildN, link.ParentN, link.HoldOut);
        var rationale = HypothesisRows.Clip(sentence + " | " + (autoRecord is null ? "owner: " + reason : autoRecord));
        var command = new ProposeHypothesisCommand(HypothesisSource.Universe, link.ParentKey, UniverseLinkProposal.Knob(link.Universe), rationale, baselineRunKey, new HypothesisGrid(), actor);
        return await hypotheses.HandleAsync(command, cancellationToken);
    }
}
