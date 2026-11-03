using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// One paired trade of protocol O (P14-09): the option version and the cash trade on the same rupee risk. <see cref="D"/> is <c>R_equiv(option) - R_equiv(cash)</c>, the unit of the study. <see cref="Net"/> is true when the option figure is net of the owner's charge lines (P14-08 pairs both
/// sides on one basis, so a net pair has a net cash figure too). The flags come from the stored row: the Black-76 fallback label, the spread from the recent archive label, a derived expiry, and lot feasibility.
/// </summary>
public sealed record ExpressionPair(DateOnly Date, long EntryEpochSeconds, long ExitEpochSeconds, string TradeKey, double OptionR, double CashR, bool Net, bool BsFallback, bool SpreadFromRecentArchive, bool ExpiryDerived, bool Sizable)
{
    /// <summary>The paired difference.</summary>
    public double D => OptionR - CashR;

    /// <summary>The pair of a stored row: the India date of the entry instant, and the flags read from the row's labels, expiry source and lot-feasibility word.</summary>
    public static ExpressionPair Of(string tradeKey, DateTimeOffset entry, DateTimeOffset exit, double optionR, double cashR, bool net, string expirySource, string sizable, IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(tradeKey);
        ArgumentNullException.ThrowIfNull(labels);
        return new ExpressionPair(
            IndiaTime.Today(entry), entry.ToUnixTimeSeconds(), exit.ToUnixTimeSeconds(), tradeKey, optionR, cashR, net,
            labels.Contains(RemarkFates.BsFallback, StringComparer.Ordinal), labels.Contains(SpreadLabel, StringComparer.Ordinal), string.Equals(expirySource, "derived", StringComparison.Ordinal), string.Equals(sizable, "sizable", StringComparison.Ordinal));
    }

    /// <summary>The label a row carries when its half-spread came from the recent archive (P14-07).</summary>
    public const string SpreadLabel = "spread_from_recent_archive";
}
