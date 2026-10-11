using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>Portfolio role of an account. Kept when the role is switched off, but inactive then. An enabled role has a default bucket.</summary>
public sealed class PortfolioSettings
{
    /// <summary>Trade history cannot start before this date.</summary>
    public static readonly DateOnly EarliestHistory = new(2000, 1, 1);

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PortfolioSettings(bool enabled, string? defaultBucket, DateOnly? historyFrom, bool includeInTotals)
    {
        Enabled = enabled;
        DefaultBucket = defaultBucket;
        HistoryFrom = historyFrom;
        IncludeInTotals = includeInTotals;
    }

    public bool Enabled { get; private set; }

    public string? DefaultBucket { get; private set; }

    /// <summary>Where the import of the trade history starts.</summary>
    public DateOnly? HistoryFrom { get; private set; }

    public bool IncludeInTotals { get; private set; }

    internal static PortfolioSettings CreateDefault() => new(false, null, null, true);

    internal Result Configure(PortfolioSpec spec, DateOnly today)
    {
        var errors = Validate(spec, today, Enabled);
        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        DefaultBucket = Clean(spec.DefaultBucket);
        HistoryFrom = spec.HistoryFrom;
        IncludeInTotals = spec.IncludeInTotals;
        return Result.Success();
    }

    /// <summary>Why the role cannot be switched on yet (field errors named like the API); empty when it can.</summary>
    internal FieldErrors CheckCanEnable()
    {
        var errors = new FieldErrors();
        if (DefaultBucket is null)
        {
            errors.Add("roles.portfolio", "Choose a default bucket before switching the role on.");
            errors.Add("portfolio.defaultBucket", "A default bucket is required.");
        }

        return errors;
    }

    /// <summary>Switches the role on or off (settings are kept). Call <see cref="CheckCanEnable"/> first when switching on.</summary>
    internal void SetEnabled(bool enabled) => Enabled = enabled;

    /// <summary>Checks a configuration; field names are relative to the portfolio object.</summary>
    internal static FieldErrors Validate(PortfolioSpec spec, DateOnly today, bool requireComplete)
    {
        var errors = new FieldErrors();
        var bucket = Clean(spec.DefaultBucket);
        if (bucket is null)
        {
            if (requireComplete)
            {
                errors.Add("defaultBucket", "A default bucket is required.");
            }
        }
        else if (!PortfolioBucket.IsValidName(bucket))
        {
            errors.Add("defaultBucket", "Not a valid bucket name.");
        }

        if (spec.HistoryFrom is { } from && (from < EarliestHistory || from > today))
        {
            errors.Add("historyFrom", $"Must be between {EarliestHistory:yyyy-MM-dd} and today.");
        }

        return errors;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
