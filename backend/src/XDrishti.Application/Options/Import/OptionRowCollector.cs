using System.Globalization;
using XDrishti.Domain.Options;

namespace XDrishti.Application.Options.Import;

/// <summary>
/// Collects the option rows of the listed underlyings while <c>DhanMasterParser</c> reads the file once (spec P14-01 criteria 1, 2 and 10): an option row of another
/// underlying is only counted, a listed row that cannot be read is counted invalid and dropped. It never adds a warning or a protected id to the master list's
/// own parse, so the instruments are imported exactly as before.
/// </summary>
internal sealed class OptionRowCollector
{
    /// <summary>The columns an option row needs beyond the ones the master list already requires.</summary>
    internal static readonly string[] Columns = ["STRIKE_PRICE", "OPTION_TYPE", "EXPIRY_FLAG"];

    private readonly OptionUnderlyings _listed;
    private readonly int _columnCount;
    private readonly int _security, _instrument, _underlying, _symbolName, _display, _lot, _expiry, _strike, _side, _tick, _flag;
    private readonly List<OptionContractRow> _rows = [];
    private long _notListed;
    private long _invalid;

    private OptionRowCollector(OptionUnderlyings listed, IReadOnlyDictionary<string, int> index, int columnCount)
    {
        _listed = listed;
        _columnCount = columnCount;
        _security = index["SECURITY_ID"];
        _instrument = index["INSTRUMENT"];
        _underlying = index["UNDERLYING_SYMBOL"];
        _symbolName = index["SYMBOL_NAME"];
        _display = index["DISPLAY_NAME"];
        _lot = index["LOT_SIZE"];
        _expiry = index["SM_EXPIRY_DATE"];
        _tick = index["TICK_SIZE"];
        _strike = index["STRIKE_PRICE"];
        _side = index["OPTION_TYPE"];
        _flag = index["EXPIRY_FLAG"];
    }

    /// <summary>The index of the <c>UNDERLYING_SYMBOL</c> column, for the cheap look at an unsplit line.</summary>
    public int UnderlyingColumn => _underlying;

    /// <summary>A collector for the header, or null with the reason when the header lacks an option column.</summary>
    public static OptionRowCollector? For(OptionUnderlyings listed, IReadOnlyDictionary<string, int> index, int columnCount, out string? unavailable)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(index);
        var missing = Columns.Where(c => !index.ContainsKey(c)).ToArray();
        unavailable = missing.Length == 0 ? null : "Dhan's file has no column " + string.Join(", ", missing) + "; no option contract was read.";
        return missing.Length == 0 ? new OptionRowCollector(listed, index, columnCount) : null;
    }

    /// <summary>The cheap look at an unsplit line's underlying: false counts the row as not listed (nothing else to do).</summary>
    public bool MightList(ReadOnlySpan<char> underlying)
    {
        if (_listed.MightList(underlying))
        {
            return true;
        }

        _notListed++;
        return false;
    }

    /// <summary>Reads a split option row (<see cref="OptionScope.MightBeOption"/> already true).</summary>
    public void Accept(string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Length != _columnCount)
        {
            _invalid++;
            return;
        }

        var instrument = fields[_instrument].Trim();
        var ticker = fields[_underlying].Trim();
        if (!OptionScope.IsOption("NSE", "D", instrument, fields[_underlying], fields[_symbolName], fields[_display])
            || ticker.Length == 0 || !_listed.Contains(OptionScope.UnderlyingSymbol(instrument, ticker)))
        {
            _notListed++;
            return;
        }

        OptionContractRow? row;
        try
        {
            row = Read(fields, instrument, ticker);
        }
        catch (ArithmeticException)
        {
            row = null; // belt and braces: no number in an option row may fail the master import
        }
        catch (FormatException)
        {
            row = null;
        }

        if (row is null)
        {
            _invalid++;
            return;
        }

        _rows.Add(row);
    }

    public OptionParse Result() => new(_rows, _notListed, _invalid, null);

    private OptionContractRow? Read(string[] f, string instrument, string ticker)
    {
        if (!long.TryParse(f[_security].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var securityId) || securityId <= 0)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(f[_expiry].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
        {
            return null;
        }

        var strike = StrikePaise(f[_strike].Trim());
        var side = f[_side].Trim();
        if (strike is null || side is not ("CE" or "PE") || !Market.Import.DhanMasterParser.TryLot(f[_lot].Trim(), out var lot))
        {
            return null;
        }

        var kind = f[_flag].Trim() switch
        {
            "W" => "week",
            "M" => "month",
            _ => null,
        };
        return new OptionContractRow(securityId, OptionScope.UnderlyingSymbol(instrument, ticker), instrument, expiry, strike.Value, side, kind, lot, TickPaise(f[_tick].Trim()));
    }

    /// <summary>Dhan's strike in rupees as integer paise, half away from zero; null unless above zero and within an int.</summary>
    internal static int? StrikePaise(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rupees))
        {
            return null;
        }

        // Checked before multiplying: decimal.TryParse accepts values up to about 7.9e28, and rupees x 100 would overflow above 7.9e26.
        if (rupees <= 0 || rupees > int.MaxValue / 100m)
        {
            return null;
        }

        var paise = decimal.Round(rupees * 100m, 0, MidpointRounding.AwayFromZero);
        return paise > 0 && paise <= int.MaxValue ? (int)paise : null;
    }

    /// <summary>Dhan's tick size is already in paise (ISS-18, verified 2026-10-19): a whole number above zero, else null.</summary>
    internal static int? TickPaise(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0 && value <= int.MaxValue && value == decimal.Truncate(value)
            ? (int)value
            : null;
}
