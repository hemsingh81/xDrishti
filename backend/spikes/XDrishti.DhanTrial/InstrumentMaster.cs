using System.Text;

namespace XDrishti.DhanTrial;

internal enum SymbolKind
{
    Equity,
    Index,
}

internal enum ResolutionStatus
{
    Found,
    Missing,
    Ambiguous,
}

internal sealed record InstrumentRow(string Exchange, string Segment, string SecurityId, string Instrument, string TradingSymbol, string SymbolName, string DisplayName, string Series);

internal sealed record Resolution(string Requested, SymbolKind Kind, ResolutionStatus Status, InstrumentRow? Row, IReadOnlyList<InstrumentRow> Candidates)
{
    /// <summary>Dhan exchangeSegment enum for the matched row (NSE_EQ for equity, IDX_I for indices).</summary>
    public string? ExchangeSegment => Row is null ? null : Kind == SymbolKind.Index ? "IDX_I" : "NSE_EQ";

    public InstrumentKey? Key => Row is null ? null : new InstrumentKey(Row.SecurityId, ExchangeSegment!, Row.Instrument);
}

/// <summary>Header-tolerant parser for Dhan's scrip master (compact or detailed). Only NSE equity and index rows are kept.</summary>
internal sealed class InstrumentMaster(IReadOnlyList<string> header, IReadOnlyList<InstrumentRow> rows, int totalRows)
{
    private static readonly string[] ExchangeNames = ["SEM_EXM_EXCH_ID", "EXCH_ID"];
    private static readonly string[] SegmentNames = ["SEM_SEGMENT", "SEGMENT"];
    private static readonly string[] SecurityNames = ["SEM_SMST_SECURITY_ID", "SECURITY_ID"];
    private static readonly string[] InstrumentNames = ["SEM_INSTRUMENT_NAME", "INSTRUMENT"];
    private static readonly string[] TradingNames = ["SEM_TRADING_SYMBOL", "TRADING_SYMBOL"];
    private static readonly string[] SymbolNames = ["SM_SYMBOL_NAME", "SYMBOL_NAME"];
    private static readonly string[] DisplayNames = ["SEM_CUSTOM_SYMBOL", "DISPLAY_NAME"];
    private static readonly string[] SeriesNames = ["SEM_SERIES", "SERIES"];

    public IReadOnlyList<string> Header => header;

    public IReadOnlyList<InstrumentRow> Rows => rows;

    public int TotalRows => totalRows;

    public static InstrumentMaster Parse(TextReader reader)
    {
        var headerLine = reader.ReadLine() ?? throw new FormatException("Instrument master is empty.");
        var header = SplitCsv(headerLine).Select(h => h.Trim().TrimStart('﻿')).ToArray();
        int Index(string[] names, bool required)
        {
            var i = Array.FindIndex(header, h => names.Contains(h, StringComparer.OrdinalIgnoreCase));
            return i < 0 && required ? throw new FormatException($"Instrument master has none of the columns [{string.Join(", ", names)}]. Header: {string.Join(", ", header)}") : i;
        }

        int exchange = Index(ExchangeNames, true), segment = Index(SegmentNames, true), security = Index(SecurityNames, true), instrument = Index(InstrumentNames, true);
        int trading = Index(TradingNames, false), symbol = Index(SymbolNames, false), display = Index(DisplayNames, false), series = Index(SeriesNames, false);
        var rows = new List<InstrumentRow>();
        var total = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            total++;
            var cells = SplitCsv(line);
            string Cell(int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : string.Empty;
            if (!string.Equals(Cell(exchange), "NSE", StringComparison.OrdinalIgnoreCase) || Cell(segment) is not ("E" or "I"))
            {
                continue;
            }

            rows.Add(new InstrumentRow(Cell(exchange), Cell(segment), Cell(security), Cell(instrument), Cell(trading), Cell(symbol), Cell(display), Cell(series)));
        }

        return new InstrumentMaster(header, rows, total);
    }

    public Resolution Resolve(string name, SymbolKind kind, IEnumerable<string>? aliases = null)
    {
        var names = new[] { name }.Concat(aliases ?? []).ToArray();
        bool Matches(InstrumentRow r) => names.Any(n => Eq(r.TradingSymbol, n) || Eq(r.SymbolName, n) || Eq(r.DisplayName, n));
        var pool = kind == SymbolKind.Equity
            ? rows.Where(r => r.Segment == "E" && string.Equals(r.Instrument, "EQUITY", StringComparison.OrdinalIgnoreCase) && (r.Series.Length == 0 || string.Equals(r.Series, "EQ", StringComparison.OrdinalIgnoreCase)))
            : rows.Where(r => r.Segment == "I" && string.Equals(r.Instrument, "INDEX", StringComparison.OrdinalIgnoreCase));
        var hits = pool.Where(Matches).ToArray();
        if (hits.Length == 1)
        {
            return new Resolution(name, kind, ResolutionStatus.Found, hits[0], hits);
        }

        if (hits.Length > 1)
        {
            return new Resolution(name, kind, ResolutionStatus.Ambiguous, null, hits);
        }

        var near = pool.Where(r => names.Any(n => r.TradingSymbol.Contains(n, StringComparison.OrdinalIgnoreCase) || r.DisplayName.Contains(n, StringComparison.OrdinalIgnoreCase))).Take(5).ToArray();
        return new Resolution(name, kind, ResolutionStatus.Missing, null, near);
    }

    private static bool Eq(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
