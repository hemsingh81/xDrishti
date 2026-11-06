using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Promotes a model to champion under the rules of CF-10 D7 (part 10d-1, ADR 0220). The actor is <c>owner:...</c> (the signed-in owner after a fresh password, checked by the endpoint; never from a body) or <c>auto</c> (only the daily run, only when <c>ml.auto_promote</c> is ON).
/// A challenger needs the promotion rule (30 graded candidates, 4 weeks, the paired bootstrap against the champion, the P6-12 counterfactual, the champion rule); a retired previous champion is re-promoted only by the owner and only if its latest evaluation still satisfies the
/// influence predicate without the switch. Promotion never turns <c>ml.influence.enabled</c> on. Nothing here places an order.
/// </summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Actor">Who asks.</param>
/// <param name="Reason">Why, 1 to 500 characters (the owner's words; the automatic run writes its own).</param>
public sealed record PromoteChallengerCommand(long ModelId, string Actor, string Reason) : ICommand;

/// <summary>What promoting did.</summary>
/// <param name="ModelId">The model.</param>
/// <param name="Role">Its role now.</param>
/// <param name="Changed">False when it was champion already and nothing was written.</param>
/// <param name="Kind"><c>promotion</c>, <c>re_promotion</c> or <c>already_champion</c>.</param>
/// <param name="Actor">Who promoted it.</param>
public sealed record PromotionOutcome(long ModelId, string Role, bool Changed, string Kind, string Actor);
