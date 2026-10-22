using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>The key of an exit choice (spec P4-06, D10): the idempotency key of the store and the name of its export folder.</summary>
public static class ExitSelectionKey
{
    /// <summary>The lower-case hex SHA-256 of the comparison (its entries and its members) and every input of D3, in a fixed text.</summary>
    /// <param name="comparisonKey">The key of the comparison the choice was made from.</param>
    /// <param name="selection">The choice.</param>
    public static string Of(string comparisonKey, ExitSelection selection)
    {
        ArgumentNullException.ThrowIfNull(comparisonKey);
        ArgumentNullException.ThrowIfNull(selection);
        var o = selection.Input.Options;
        var text = "xd-exit-selection 1\ncomparison=" + comparisonKey
            + "\nentries=" + selection.Input.EntriesKey
            + "\nmin_train_months=" + o.MinTrainMonths.ToString(CultureInfo.InvariantCulture)
            + "\ntest_months=" + o.TestMonths.ToString(CultureInfo.InvariantCulture)
            + "\nembargo_sessions=" + o.EmbargoSessions.ToString(CultureInfo.InvariantCulture)
            + "\nlambda=" + CanonicalText.Number(o.Lambda)
            + "\nmin_trades=" + o.MinTrades.ToString(CultureInfo.InvariantCulture)
            + "\nprefer_plateau=" + (o.PreferPlateau ? "true" : "false")
            + "\nstable_folds_pct=" + CanonicalText.Number(o.StableFoldsPct) + "\n";
        return CanonicalText.Sha256Hex(text);
    }
}
