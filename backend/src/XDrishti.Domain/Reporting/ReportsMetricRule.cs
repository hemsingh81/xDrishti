using System.Globalization;

namespace XDrishti.Domain.Reporting;

/// <summary>The state rule of a reported figure (P8-00 D3, criterion 3): a number exists only in the state value or provisional and is finite; every other state has no number and a reason.</summary>
public static class ReportsMetricRule
{
    private static readonly HashSet<string> Known = new(["value", "provisional", "insufficient", "unavailable", "not_evaluable", "empty"], StringComparer.Ordinal);

    /// <summary>What is wrong with the figure, or null when it keeps the rule.</summary>
    /// <param name="state">The state word.</param>
    /// <param name="number">The number, if any.</param>
    /// <param name="reason">The reason, if any.</param>
    public static string? Break(string? state, double? number, string? reason)
    {
        if (state is null || !Known.Contains(state))
        {
            return string.Create(CultureInfo.InvariantCulture, $"unknown state '{state}'");
        }

        if (state is "value" or "provisional")
        {
            return number is { } n && double.IsFinite(n) ? null : $"state {state} without a finite number";
        }

        if (number is not null)
        {
            return $"a number in state {state}";
        }

        return string.IsNullOrWhiteSpace(reason) ? $"state {state} without a reason" : null;
    }
}
