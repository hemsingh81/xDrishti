using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Counts the unknown values of extracted rows per feature and per closed reason, and the known values built on an unverified parser, and writes them as JSON in a fixed order (P15-05): <c>{"nan":{feature:{reason:n}},"parser_unverified":{feature:n}}</c>. Pure.</summary>
public static class ExtensionNanCounts
{
    public static string Json(IReadOnlyList<string> names, IEnumerable<IReadOnlyList<FeatureCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(rows);
        var nan = names.Select(_ => new SortedDictionary<string, int>(StringComparer.Ordinal)).ToArray();
        var unverified = new int[names.Count];
        foreach (var row in rows)
        {
            for (var i = 0; i < names.Count && i < row.Count; i++)
            {
                if (row[i].IsKnown)
                {
                    unverified[i] += row[i].Unverified ? 1 : 0;
                }
                else
                {
                    var reason = row[i].Reason ?? "unknown";
                    nan[i][reason] = nan[i].GetValueOrDefault(reason) + 1;
                }
            }
        }

        var text = new StringBuilder("{\"nan\":{");
        for (var i = 0; i < names.Count; i++)
        {
            text.Append(i == 0 ? "" : ",").Append('"').Append(names[i]).Append("\":{")
                .Append(string.Join(',', nan[i].Select(p => "\"" + p.Key + "\":" + p.Value.ToString(CultureInfo.InvariantCulture)))).Append('}');
        }

        text.Append("},\"parser_unverified\":{");
        for (var i = 0; i < names.Count; i++)
        {
            text.Append(i == 0 ? "" : ",").Append('"').Append(names[i]).Append("\":").Append(unverified[i].ToString(CultureInfo.InvariantCulture));
        }

        return text.Append("}}").ToString();
    }
}
