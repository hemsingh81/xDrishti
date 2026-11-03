using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// The trial of protocol O (P14-09, decision 1): one expression profile evaluated on one cash population is one look in family <c>expr:option_buy</c>. The text names the profile hash and the population (the stored cash run and the out-of-sample rule), not the data window, so evaluating the same
/// thing again does not grow N; the ledger run id includes the window (prefix <c>p14-ledger/1</c>), so a new window is a new row with the same hash. The caller appends the row <b>before</b> any figure is computed.
/// </summary>
public static class ExpressionTrial
{
    /// <summary>The prefix of the ledger run id.</summary>
    public const string RunIdPrefix = "p14-ledger/1";

    /// <summary>The canonical text of the trial.</summary>
    public static string Text(string profileHash, string cashRunKey)
    {
        ArgumentNullException.ThrowIfNull(profileHash);
        ArgumentNullException.ThrowIfNull(cashRunKey);
        return string.Join('\n', "expr-trial/1", "profile:" + profileHash, "population:cash run " + cashRunKey + ", out-of-sample test windows (oos-windows/1), paired trades", "unit:R_equiv(option) - R_equiv(cash), same rupee risk", "protocol:O");
    }

    /// <summary>The trial of a profile on the cash run, with the data window of the run.</summary>
    public static ExtensionTrialId Of(string family, string profileHash, string cashRunKey, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(family);
        var text = Text(profileHash, cashRunKey);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(family + "\n" + text)));
        var window = string.Create(CultureInfo.InvariantCulture, $"{RunIdPrefix}\n{hash}\n{from:yyyy-MM-dd}\n{to:yyyy-MM-dd}");
        return new ExtensionTrialId(family, text, hash, new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(window)).AsSpan(0, 16)));
    }
}
