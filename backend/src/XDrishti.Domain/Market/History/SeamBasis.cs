using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Market.History;

/// <summary>
/// The basis a seam verification rests on (CF-04d decision 3): the SHA-256 of <c>seam instant | each applied action as ex-date:kind:numerator:denominator:onCsv</c>, where <c>onCsv</c> is 1 when the ex-date is on or before the date the CSV files are adjusted through. The probe and the continuity check store
/// it; admission needs both stored values to equal the current one. Adding, editing or undoing an action, a moved seam or a CSV load that crosses an ex-date changes it; a CSV load that moves <c>csvAdjustedThrough</c> without crossing an ex-date does not. Pure.
/// </summary>
public static class SeamBasis
{
    public static string Of(DateTimeOffset seam, IEnumerable<AppliedAction> actions, DateOnly? csvAdjustedThrough)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var text = new StringBuilder();
        text.Append(seam.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        foreach (var action in actions.OrderBy(a => a.ExDate).ThenBy(a => a.Kind))
        {
            var onCsv = csvAdjustedThrough is { } through && action.ExDate <= through;
            text.Append('|')
                .Append(action.ExDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(':')
                .Append(action.Kind.ToString()).Append(':')
                .Append(action.Numerator.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(action.Denominator.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(onCsv ? '1' : '0');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
