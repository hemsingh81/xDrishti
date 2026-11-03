using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Infrastructure.OptionCosts;

/// <summary>
/// The settings <c>Options:Costs:*</c> of the half-spread model (P14-07, ADR 0194), bound on every host that builds or reads a model; bounds are checked at start-up. <see cref="AssumedHalfSpreadPct"/> is the owner's own assumption for the time before an
/// underlying has enough usable sessions, keyed by the short symbol (<c>NIFTY</c>; the exchange prefix would break the configuration path) in percent of the mid; it is empty by default and nothing in the repository fills it.
/// </summary>
internal sealed partial class OptionCostsOptions : IValidatableObject
{
    public const string SectionName = "Options:Costs";

    /// <summary>The usable chain sessions an underlying needs before the model is measured.</summary>
    [Required]
    [Range(20, 250)]
    public int? MinUsableSessions { get; set; } = 60;

    /// <summary>The quotes a bucket needs before its own median is used.</summary>
    [Required]
    [Range(50, 5000)]
    public int? MinQuotesPerBucket { get; set; } = 200;

    /// <summary>The owner's assumed half-spread per short symbol, 0.01 to 20 percent. Empty by default.</summary>
    public Dictionary<string, decimal> AssumedHalfSpreadPct { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (symbol, pct) in AssumedHalfSpreadPct)
        {
            if (!SymbolShape().IsMatch(symbol))
            {
                yield return new ValidationResult($"AssumedHalfSpreadPct has the key '{symbol}': use the short symbol, letters, digits, '&', '-' or '_' only (for example NIFTY).", [nameof(AssumedHalfSpreadPct)]);
            }

            if (pct is < 0.01m or > 20m)
            {
                yield return new ValidationResult(string.Create(CultureInfo.InvariantCulture, $"AssumedHalfSpreadPct for '{symbol}' must be from 0.01 to 20 percent."), [nameof(AssumedHalfSpreadPct)]);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9&_-]{1,30}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SymbolShape();
}
