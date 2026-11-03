namespace XDrishti.Domain.Learning;

/// <summary>The stored names of <see cref="Expression"/> (<c>cash</c>, <c>future</c>, <c>option_buy</c>, ...): the text of the eighth cell-key part and of <c>lab.expression_profiles.kind</c>.</summary>
public static class ExpressionNames
{
    private static readonly (Expression Value, string Name)[] Table =
    [
        (Expression.Cash, "cash"),
        (Expression.Future, "future"),
        (Expression.OptionBuy, "option_buy"),
        (Expression.DebitSpread, "debit_spread"),
        (Expression.CreditSpread, "credit_spread"),
        (Expression.IronCondor, "iron_condor"),
        (Expression.Pair, "pair"),
        (Expression.Basis, "basis"),
    ];

    /// <summary>Every stored name, in the order of the enum.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Table.Select(t => t.Name)];

    /// <summary>The stored name.</summary>
    public static string Code(this Expression expression)
    {
        foreach (var (value, name) in Table)
        {
            if (value == expression)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(expression), expression, "Not a known expression.");
    }

    /// <summary>Reads a stored name back (exact, lower case); null when it is not one of the closed set.</summary>
    public static Expression? Parse(string? name)
    {
        foreach (var (value, stored) in Table)
        {
            if (string.Equals(stored, name, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }
}
