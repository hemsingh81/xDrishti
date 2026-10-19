using System.Globalization;
using System.Text;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>
/// <c>assist-log.csv</c> (spec P3-09c D6): the columns of <c>check-log.csv</c>, one row per trade of the pack, with the reader's readings in the <c>seen_*</c> columns, the second source's bar of the entry minute in <c>ext_*</c>, and <c>auto:&lt;second source&gt;</c> in
/// <c>chart</c> (the column that says where a row was read). <c>verdict</c>, <c>finding</c> and <c>signed_on</c> are empty: the tool never signs. Spots are the owner's and are not in this file.
/// </summary>
internal static class AssistLog
{
    private const string IstFormat = "yyyy-MM-dd HH:mm";

    public static string Text(IReadOnlyList<AssistRow> rows)
    {
        var text = new StringBuilder(string.Join(',', PackFiles.CheckLogColumns)).Append('\n');
        foreach (var row in rows)
        {
            var r = row.Reading;
            var note = row.Unavailable is not null ? "source_unavailable: " + row.Unavailable : r is null ? "reader found no trade" : string.Empty;
            string[] cells =
            [
                row.Pack.Seq.ToString(CultureInfo.InvariantCulture), row.Pack.TradeKey, "trade", row.Source,
                r is null ? string.Empty : Time(r.EntryEpoch), r is null ? string.Empty : Number(r.EntryPrice), r is null ? string.Empty : Number(r.Stop), r is null ? string.Empty : Number(r.Target),
                r is null ? string.Empty : Time(r.ExitEpoch), r is null ? string.Empty : Number(r.ExitPrice), r is null ? string.Empty : r.ExitReason.Code(), r is null ? string.Empty : Number(Math.Round(r.SeenR, 4)),
                string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, note.Replace(',', ';'), string.Empty,
            ];
            text.Append(string.Join(',', cells)).Append('\n');
        }

        return text.ToString();
    }

    public static string Time(long epoch) => DateTimeOffset.FromUnixTimeSeconds(epoch).ToOffset(TimeSpan.FromMinutes(330)).ToString(IstFormat, CultureInfo.InvariantCulture);

    public static string Number(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
