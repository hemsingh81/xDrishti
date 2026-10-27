namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// The kinds of proposal the model may make (spec P9-08 AC 2). There is no kind that tracks an instrument (it would start a history backfill; the pack has no engine evidence for untracked instruments) and none that places an order. <c>next_day_focus</c> is not built in this slice
/// (cut by the spec's cut order, ADR 0142).
/// </summary>
public static class ProposalKind
{
    public const string Hypothesis = "hypothesis";
    public const string BasketAdd = "basket_add";
    public const string BasketRemove = "basket_remove";

    /// <summary>Every kind built, in the order the list shows them.</summary>
    public static IReadOnlyList<string> All { get; } = [Hypothesis, BasketAdd, BasketRemove];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);

    public static bool IsBasket(string kind) => kind is BasketAdd or BasketRemove;
}
