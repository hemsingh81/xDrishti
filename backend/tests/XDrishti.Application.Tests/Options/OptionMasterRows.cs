using XDrishti.Application.Tests.Market;

namespace XDrishti.Application.Tests.Options;

/// <summary>
/// Option rows copied from Dhan's real <c>api-scrip-master-detailed.csv</c> (read 2026-11-01), appended to the committed master sample. The sample itself already
/// holds three RELIANCE stock options (ids 106474 to 106476), a BSE stock option and a currency option.
/// </summary>
internal static class OptionMasterRows
{
    public const string NiftyWeeklyCall = "NSE,D,35008,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-CE,NIFTY 09 NOV 17350 CALL,OP,NA,65.0,2026-11-09,17350.00000,CE,5.0000,W,N,N,N,NA,A,0,0,0,0,0,0,0,6313.0000,5153.9000,1756,";

    public const string NiftyWeeklyPut = "NSE,D,35009,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-PE,NIFTY 09 NOV 17350 PUT,OP,NA,65.0,2026-11-09,17350.00000,PE,5.0000,W,N,N,N,NA,A,0,0,0,0,0,0,0,20.0500,0.0500,1756,";

    public const string NiftyMonthlyCall = "NSE,D,35062,NA,OPTIDX,26000,NIFTY,NIFTY-Nov2026-17350-CE,NIFTY 23 NOV 17350 CALL,OP,NA,65.0,2026-11-23,17350.00000,CE,5.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,6355.3500,5196.3500,1756,";

    public const string BankNiftyCall = "NSE,D,35000,NA,OPTIDX,26009,BANKNIFTY,BANKNIFTY-Dec2026-69400-CE,BANKNIFTY 29 DEC 69400 CALL,OP,NA,30.0,2026-12-29,69400.00000,CE,5.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,22.0000,0.0500,901,";

    public const string CromptonHalfStrike = "NSE,D,100325,NA,OPTSTK,17094,CROMPTON,CROMPTON-Nov2026-147.5-CE,CROMPTON 23 NOV 147.50 CALL,OP,NA,2150.0,2026-11-23,147.50000,CE,1.0000,M,N,N,N,NA,A,0,0,0,0,0,0,0,64.6200,24.6200,116101,";

    public static readonly string[] All = [NiftyWeeklyCall, NiftyWeeklyPut, NiftyMonthlyCall, BankNiftyCall, CromptonHalfStrike];

    /// <summary>The master sample with the rows above (or the given ones) appended.</summary>
    public static string Master(params string[] rows)
    {
        var text = MasterFixtures.Text(MasterFixtures.Master).TrimEnd('\n', '\r');
        return text + "\n" + string.Join('\n', rows.Length == 0 ? All : rows) + "\n";
    }

    /// <summary>A real row with one field replaced (0-based column).</summary>
    public static string With(string row, int column, string value)
    {
        var fields = row.Split(',');
        fields[column] = value;
        return string.Join(',', fields);
    }
}
