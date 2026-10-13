namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// An item a rule produced, before the owner's decisions are applied (its state is <c>open</c>). <see cref="Size"/> orders items of the same severity (the larger the trigger, the earlier); <see cref="BucketIds"/> are the buckets of the holdings the item is
/// about, for the bucket scope.
/// </summary>
internal sealed record RuleHit(ActionKey Key, ActionItem Item, decimal Size, IReadOnlyList<Guid> BucketIds);
