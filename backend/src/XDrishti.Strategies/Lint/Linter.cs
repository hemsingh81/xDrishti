using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// The look-ahead and timeframe rules over a bound tree (spec P3-02, <i>Lint rules</i>), and the facts the plan is made of. It runs on a tree that has binder errors too (the parts that could not be bound are
/// skipped), so a condition with several faults gets all of them at once. The rules: <see cref="LagRules"/> (the history a condition needs, <c>lag_too_deep</c>, <c>mixed_timeframes_in_lag</c> and the warning <c>lag_across_session</c>: a read of a per-session feature from further back than a session holds),
/// <see cref="TimeRule"/> (<c>time_on_daily</c>), <see cref="ConstantRule"/> (<c>constant_condition</c>) and <see cref="Requirements"/> (<c>needs_context</c>, <c>needs_expiry_days</c>, reported only for a
/// condition without errors). The rules that need the names
/// or the text (<c>unknown_name</c>, <c>bad_arguments</c>, <c>unsupported_timeframe</c>, <c>negative_or_zero_lag</c>, <c>lag_on_non_feature</c>, <c>bad_time_literal</c>, <c>type_mismatch</c>,
/// <c>redundant_timeframe</c>) are found by the binder while it reads them, and all share <see cref="LintCodes"/>.
/// </summary>
internal static class Linter
{
    /// <summary>Applies every rule to the tree, adds the problems to the scope and returns the facts.</summary>
    public static LintFacts Run(IBoundExpression root, BindingScope scope)
    {
        var leaves = new List<BoundFeature>();
        BoundTree.CollectFeatures(root, leaves);
        var features = new List<FeatureSpec>();
        foreach (var leaf in leaves)
        {
            // each spec once, in the order it first appears (a list searched in order: a condition reads a handful of features)
            if (!features.Exists(f => f.Equals(leaf.Spec)))
            {
                features.Add(leaf.Spec);
            }
        }


        TimeRule.Apply(root, scope);
        var depth = LagRules.Apply(root, scope);
        ConstantRule.Apply(root, scope);

        // last, because the notes are for a condition that can be used: with an error there is no plan to need anything
        var (needsContext, needsExpiryDays) = Requirements.Apply(leaves, scope);
        return new LintFacts(features.AsReadOnly(), depth, needsContext, needsExpiryDays);
    }
}
