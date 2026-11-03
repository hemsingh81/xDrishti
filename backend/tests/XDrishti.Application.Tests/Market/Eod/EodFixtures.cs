using System.Text;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>
/// Synthetic files in the best-known NSE layouts. TO BE VERIFIED BY THE PROBE (<c>xd data eod-probe</c>, CF-04 Part A step 0): the header names and the date formats below are remembered layouts, not captured files. When the probe has run, replace these with
/// the captured header lines (rows stay synthetic) and keep the tests.
/// </summary>
internal static class EodFixtures
{
    public const string LegacyHeader = "SYMBOL,SERIES,OPEN,HIGH,LOW,CLOSE,LAST,PREVCLOSE,TOTTRDQTY,TOTTRDVAL,TIMESTAMP,TOTALTRADES,ISIN,";

    public const string UdiffHeader = "TradDt,BizDt,Sgmt,Src,FinInstrmTp,FinInstrmId,ISIN,TckrSymb,SctySrs,XpryDt,FininstrmActlXpryDt,StrkPric,OptnTp,FinInstrmNm,OpnPric,HghPric,LwPric,ClsPric,LastPric,PrvsClsgPric,UndrlygPric,SttlmPric,OpnIntrst,ChngInOpnIntrst,TtlTradgVol,TtlTrfVal,TtlNbOfTxsExctd,SsnId,NewBrdLotQty,Rmks,Rsvd1,Rsvd2,Rsvd3,Rsvd4";

    public static readonly DateOnly Day = new(2024, 7, 5);

    public static byte[] Legacy(params string[] rows) => Bytes(LegacyHeader, rows);

    public static byte[] Udiff(params string[] rows) => Bytes(UdiffHeader, rows);

    public static string LegacyRow(string symbol, string series, string date = "05-JUL-2024", long traded = 10_000, string close = "100.50") =>
        $"{symbol},{series},99.00,101.00,98.50,{close},100.40,99.50,{traded},1005000.00,{date},321,INE000A01010,";

    public static string UdiffRow(string symbol, string series, string date = "2024-07-05", long traded = 10_000, string close = "100.50") =>
        $"{date},{date},CM,NSE,STK,1,INE000A01010,{symbol},{series},,,,,{symbol} LTD,99.00,101.00,98.50,{close},100.40,99.50,,100.50,,,{traded},1005000.00,321,F1,1,,,,,";

    /// <summary>A delivery (MTO) file with its preamble lines and one data line per argument ("symbol,series,traded,delivered,percent").</summary>
    public static byte[] Delivery(params string[] lines) => Encoding.UTF8.GetBytes(
        "Security Wise Delivery Position - Compulsory Rolling Settlement\n10,MTO,05072024,1234567,0001\nTrade Date <05-JUL-2024>,Settlement Type <N>,Settlement No <2024123>\n"
        + "Record Type,Sr No,Name of Security,Type of Security,Quantity Traded,Deliverable Quantity(gross across client level),% of Deliverable Quantity to Traded Quantity\n"
        + string.Join('\n', lines.Select((l, i) => { var p = l.Split(','); return $"20,{i + 1},{p[0]},{p[1]},{p[2]},{p[3]},{p[4]}"; })) + "\n");

    private static byte[] Bytes(string header, string[] rows) => Encoding.UTF8.GetBytes(header + "\n" + string.Join('\n', rows) + "\n");
}
