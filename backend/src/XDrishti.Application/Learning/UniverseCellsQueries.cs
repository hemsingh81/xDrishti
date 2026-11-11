using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The tier cells (universe <c>liq_high</c>, <c>liq_mid</c>, <c>liq_low</c>) of the newest snapshot that holds any (BK-11, ADR 0261), optionally of one strategy and one window; empty before a computation with the universe split. <c>learning.invalid</c> for another window code.</summary>
public sealed record ListUniverseCellsQuery(string? Strategy, string? Window) : IQuery<Result<IReadOnlyList<CellStatsView>>>;

internal sealed class ListUniverseCellsHandler(ICellStatsStore store) : IQueryHandler<ListUniverseCellsQuery, Result<IReadOnlyList<CellStatsView>>>
{
    public async ValueTask<Result<IReadOnlyList<CellStatsView>>> HandleAsync(ListUniverseCellsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Window is not null && CellWindowExtensions.Parse(query.Window) is null)
        {
            return Result.Failure<IReadOnlyList<CellStatsView>>(LearningErrors.Invalid([new FieldError("window", "The window is all, 250d or 60d.")]));
        }

        var rows = await store.ListUniverseLatestAsync(string.IsNullOrWhiteSpace(query.Strategy) ? null : query.Strategy, query.Window, cancellationToken);
        return Result.Success<IReadOnlyList<CellStatsView>>([.. rows.Where(r => r.Row.Cell.Universe != CellId.AllUniverses).Select(CellStatsView.Of)]);
    }
}

/// <summary>One tier cell read against its all-shares parent (BK-11, criteria 5, 6 and 11).</summary>
/// <param name="ChildKey">The tier cell's key.</param>
/// <param name="ParentKey">The all-shares cell's key.</param>
/// <param name="Universe">The tier.</param>
/// <param name="Window">The window code.</param>
/// <param name="AsOf">The snapshot's as-of date.</param>
/// <param name="Reason"><c>child_better</c>, <c>no_better_than_parent</c> or <c>child_thin</c>.</param>
/// <param name="ReadKey">The key a reader takes: the child's when it is better, else the parent's.</param>
/// <param name="ChildN">Trades of the tier cell.</param>
/// <param name="ParentN">Trades of the parent.</param>
/// <param name="ChildLower">The tier cell's lower credible bound of the win rate.</param>
/// <param name="ParentLower">The parent's bound without the child's trades; null when the child is thin.</param>
/// <param name="Observation">True when the tier is read in place of the parent and has at least the link minimum of trades: an observation to show, never applied.</param>
/// <param name="HoldOut"><c>stable</c> (the tier's lower bound beats the rest of the parent's in both halves of its trades), <c>unstable</c>, <c>insufficient</c> (a half has fewer than 30 trades) or <c>not_evaluated</c> (a row stored before it); computed when the cells were computed.</param>
/// <param name="HoldOutFirstN">Tier trades in the first half; null when not evaluated.</param>
/// <param name="HoldOutSecondN">Tier trades in the second half; null when not evaluated.</param>
/// <param name="Text">The sentence the Setups view shows.</param>
public sealed record UniverseLinkView(string ChildKey, string ParentKey, string Universe, string Window, DateOnly AsOf, string Reason, string ReadKey, int ChildN, int ParentN, double ChildLower, double? ParentLower, bool Observation, string HoldOut, int? HoldOutFirstN, int? HoldOutSecondN, string Text);

/// <summary>For each tier cell of the newest snapshot, which cell a reader takes and why (BK-11). Reads only; nothing here applies a restriction.</summary>
public sealed record GetUniverseLinksQuery(string? Strategy) : IQuery<Result<IReadOnlyList<UniverseLinkView>>>;

internal sealed class GetUniverseLinksHandler(ICellStatsStore store, IUniverseSettingsStore settingsStore) : IQueryHandler<GetUniverseLinksQuery, Result<IReadOnlyList<UniverseLinkView>>>
{
    public async ValueTask<Result<IReadOnlyList<UniverseLinkView>>> HandleAsync(GetUniverseLinksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await store.ListUniverseLatestAsync(string.IsNullOrWhiteSpace(query.Strategy) ? null : query.Strategy, null, cancellationToken);
        var byKey = rows.ToDictionary(r => (r.Row.Cell, r.Row.Window));
        var settings = (await settingsStore.CurrentAsync(cancellationToken)).Settings;
        var minChild = settings.MinTradesChild;
        var minLink = settings.LinkMinTrades;
        var views = new List<UniverseLinkView>();
        foreach (var child in rows.Where(r => r.Row.Cell.Universe != CellId.AllUniverses))
        {
            if (child.Row.Cell.Parent() is not { } parentId || !byKey.TryGetValue((parentId, child.Row.Window), out var parent))
            {
                continue;
            }

            var choice = CellChoice.Resolve(child.Row, parent.Row, minChild, child.Options);
            var hold = child.Row.UniverseHoldOut;
            var observation = choice.IsChild && child.Row.N >= minLink && hold is { IsStable: true };
            views.Add(new UniverseLinkView(
                child.Row.Cell.Key, parentId.Key, child.Row.Cell.Universe, child.Row.Window.Code(), child.Row.AsOf, choice.Reason, choice.Cell.Cell.Key, child.Row.N, parent.Row.N, choice.ChildLower, choice.ParentLower, observation, hold?.Verdict ?? "not_evaluated", hold?.FirstN, hold?.SecondN, Text(child.Row, parent.Row, choice)));
        }

        return Result.Success<IReadOnlyList<UniverseLinkView>>([.. views.OrderBy(v => v.ChildKey, StringComparer.Ordinal).ThenBy(v => v.Window, StringComparer.Ordinal)]);
    }

    private static string Text(CellStatsRow child, CellStatsRow parent, CellChoiceResult choice)
    {
        var who = $"{child.Cell.Strategy}, {child.Cell.Timeframe}, {child.Cell.Side}, {child.Cell.Style}, {child.Cell.Universe}";
        return choice.Reason switch
        {
            CellChoice.ChildBetter => string.Create(CultureInfo.InvariantCulture, $"{who}: stronger than all shares (lower bound {choice.ChildLower:0.00} vs {choice.ParentLower:0.00}, n {child.N}/{parent.N}); an observation, nothing is applied (hold-out {child.UniverseHoldOut?.Verdict ?? "not_evaluated"})"),
            CellChoice.NoBetterThanParent => string.Create(CultureInfo.InvariantCulture, $"{who}: no better than all shares (lower bound {choice.ChildLower:0.00} vs {choice.ParentLower:0.00}, n {child.N}/{parent.N})"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{who}: not enough evidence (n {child.N})"),
        };
    }
}
