using XDrishti.Domain.Common;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// What the indicator catalog says about one feature spec: the field errors the frame's own validation gives for a frame of this one feature (a parameter outside its range, a combination such as <c>fast</c> not below
/// <c>slow</c>, an opening range that is not whole bars, a timeframe the feature cannot run on), without the ones about the frame's options (a context or expiry dates are the caller's to give). One rule for the binder of a
/// condition and for the compile of a stop and of an exit (spec P3-02b): periods, whole numbers, whole bars and timeframes are never copied.
/// </summary>
internal static class CatalogCheck
{
    /// <summary>The field errors of the spec; empty when it is valid. The fields are those of the frame's validation (<c>specs[0].p1</c>, <c>specs[0].timeframe</c>).</summary>
    public static IReadOnlyList<FieldError> Answer(FeatureSpec spec)
    {
        var created = FeatureFrame.Create([spec]);
        return created.IsSuccess
            ? []
            : [.. (created.Error.Fields ?? []).Where(f => !f.Field.StartsWith("options.", StringComparison.Ordinal))];
    }

    /// <summary>The part of a field name after the first dot: <c>p1</c> for <c>specs[0].p1</c>, <c>timeframe</c> for <c>specs[0].timeframe</c>.</summary>
    public static string FieldOf(FieldError error) => error.Field[(error.Field.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
