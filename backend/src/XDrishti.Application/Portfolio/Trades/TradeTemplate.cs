namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The template of a tradebook file (ADR 0023): the canonical header and two example rows (made-up security and prices) that the reader accepts as they are. Pure.</summary>
public static class TradeTemplate
{
    public const string FileName = "xdrishti-trades-template.csv";

    public const string Csv =
        "Date,Symbol,ISIN,Side,Quantity,Price,Exchange,Trade ID,Time,Charges\n"
        + "2024-04-15,EXMPL,INE000A01011,BUY,10,1250.50,NSE,T-0001,09:21:05,12.35\n"
        + "2024-09-20,EXMPL,INE000A01011,SELL,4,1410.25,NSE,T-0002,14:40:00,9.80\n";
}
