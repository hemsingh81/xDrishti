using System.Globalization;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The column mapping of <c>research.sim_trades</c> (spec P3-10, D3 and D8), written once: the 33 fields of <see cref="SimTrade"/> (less its schema version, which the run carries, and the sheet id of the costing, which the run carries) and the
/// baskets of the <see cref="RunTrade"/>, as typed columns in one order. The model, the migration, the binary copy, the reads and the generated checks all read this list. A test compares its names with the properties of <see cref="SimTrade"/>
/// and <see cref="TradeCosting"/>, so a field added to the Domain fails there until the mapping and a migration are written.
/// </summary>
internal static class TradeColumns
{
    /// <summary>The four key columns that come before the mapped ones in the copy and in the reads.</summary>
    public const string RunId = "run_id";

    public const string OpenAtEnd = "open_at_end";

    public const string Ordinal = "ordinal";

    public const string TradeKey = "trade_key";

    /// <summary>The mapped columns in table order.</summary>
    public static readonly IReadOnlyList<TradeColumn> Columns = All();

    /// <summary>The 24 costing columns, the last of the list (5 leading, 16 charge lines, 3 trailing); all null or all present.</summary>
    public static readonly IReadOnlyList<string> CostingNames = [.. Columns.TakeLast(CostingCount).Select(c => c.Name)];

    /// <summary>How many columns the costing takes.</summary>
    public const int CostingCount = 24;

    private static readonly Dictionary<string, int> Index = Columns.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

    /// <summary>A <see cref="RunTrade"/> from the values of one row, in the order of <see cref="Columns"/>; the schema is the current one (the caller checked the run's) and the sheet id of the costing is the run's.</summary>
    public static RunTrade Materialise(object?[] v, string? costsSheetId)
    {
        ArgumentNullException.ThrowIfNull(v);
        T Get<T>(string name) => (T)v[Index[name]]!;
        T? Opt<T>(string name)
            where T : struct => v[Index[name]] is T value ? value : null;

        TradeCosting? costing = null;
        if (v[Index["cost_segment"]] is CostSegment segment)
        {
            LegCharges Leg(string prefix) => new(
                Get<decimal>(prefix + "_brokerage"), Get<decimal>(prefix + "_stt"), Get<decimal>(prefix + "_exchange_txn"), Get<decimal>(prefix + "_sebi_fee"),
                Get<decimal>(prefix + "_stamp_duty"), Get<decimal>(prefix + "_other_charges"), Get<decimal>(prefix + "_dp_charge"), Get<decimal>(prefix + "_gst"));
            costing = new TradeCosting(
                segment, costsSheetId ?? throw new StoreReadException("A costed trade is stored in a run that has no sheet."), Get<long>("cost_quantity"), Get<decimal>("cost_risk_taken"),
                Get<decimal>("cost_entry_turnover"), Get<decimal>("cost_exit_turnover"), Leg("entry"), Leg("exit"), Get<decimal>("cost_total"), Get<double>("cost_r"), Get<double>("r_net"));
        }

        var trade = new SimTrade(
            SimTrade.CurrentSchemaVersion,
            Get<string>("strategy_id"),
            Get<Guid>("instrument_id"),
            Get<string>("symbol"),
            Get<Side>("side"),
            Get<DateOnly>("trade_date"),
            Get<long>("decision_epoch_seconds"),
            Get<Timeframe>("trigger_timeframe"),
            new Bar(Get<long>("trigger_bar_epoch"), Get<double>("trigger_bar_open"), Get<double>("trigger_bar_high"), Get<double>("trigger_bar_low"), Get<double>("trigger_bar_close"), Get<long>("trigger_bar_volume")),
            Get<int>("trigger_bar_minutes"),
            Get<bool>("trigger_bar_complete"),
            Get<long>("entry_epoch_seconds"),
            Get<double>("entry_price"),
            Get<double>("stop_price"),
            Opt<double>("target_price"),
            Get<long>("exit_epoch_seconds"),
            Get<double>("exit_price"),
            Get<ExitReason>("exit_reason"),
            Get<double>("r_exact"),
            Get<double>("mae_r"),
            Get<double>("mfe_r"),
            Get<FillBasis>("fills"),
            Get<CostBasis>("costs"),
            v[Index["entry_snapshot_json"]] as string,
            Get<double>("r_filled"),
            Get<double>("slippage_r"),
            Get<double>("entry_filled_price"),
            Get<double>("exit_filled_price"),
            Get<EntryFill>("entry_fill"),
            Get<bool>("tick_fallback"),
            Get<int>("sessions_held"),
            Get<int>("sessions_unseen"),
            costing);
        return new RunTrade(trade, Get<Guid[]>("basket_ids"));
    }

    private static List<TradeColumn> All()
    {
        var columns = new List<TradeColumn>
        {
            Str("strategy_id", 64, t => t.Trade.StrategyId),
            Uid("instrument_id", t => t.Trade.InstrumentId),
            Str("symbol", 40, t => t.Trade.Symbol),
            Enum<Side>("side", t => t.Trade.Side),
            Dat("trade_date", t => t.Trade.TradeDate),
            Lng("decision_epoch_seconds", t => t.Trade.DecisionEpochSeconds),
            Enum<Timeframe>("trigger_timeframe", t => t.Trade.TriggerTimeframe),
            Lng("trigger_bar_epoch", t => t.Trade.TriggerBar.EpochSeconds),
            Dbl("trigger_bar_open", t => t.Trade.TriggerBar.Open),
            Dbl("trigger_bar_high", t => t.Trade.TriggerBar.High),
            Dbl("trigger_bar_low", t => t.Trade.TriggerBar.Low),
            Dbl("trigger_bar_close", t => t.Trade.TriggerBar.Close),
            Lng("trigger_bar_volume", t => t.Trade.TriggerBar.Volume),
            Int("trigger_bar_minutes", t => t.Trade.TriggerBarMinutes),
            Bln("trigger_bar_complete", t => t.Trade.TriggerBarComplete),
            Lng("entry_epoch_seconds", t => t.Trade.EntryEpochSeconds),
            Dbl("entry_price", t => t.Trade.EntryPrice),
            Dbl("stop_price", t => t.Trade.StopPrice),
            Dbl("target_price", t => t.Trade.TargetPrice, nullable: true),
            Lng("exit_epoch_seconds", t => t.Trade.ExitEpochSeconds),
            Dbl("exit_price", t => t.Trade.ExitPrice),
            Enum<ExitReason>("exit_reason", t => t.Trade.ExitReason),
            Dbl("r_exact", t => t.Trade.RExact),
            Dbl("mae_r", t => t.Trade.MaeR),
            Dbl("mfe_r", t => t.Trade.MfeR),
            Enum<FillBasis>("fills", t => t.Trade.Fills),
            Enum<CostBasis>("costs", t => t.Trade.Costs),
            Txt("entry_snapshot_json", t => t.Trade.EntrySnapshotJson),
            Dbl("r_filled", t => t.Trade.RFilled),
            Dbl("slippage_r", t => t.Trade.SlippageR),
            Dbl("entry_filled_price", t => t.Trade.EntryFilledPrice),
            Dbl("exit_filled_price", t => t.Trade.ExitFilledPrice),
            Enum<EntryFill>("entry_fill", t => t.Trade.EntryFill),
            Bln("tick_fallback", t => t.Trade.TickFallback),
            Int("sessions_held", t => t.Trade.SessionsHeld),
            Int("sessions_unseen", t => t.Trade.SessionsUnseen),
            Uids("basket_ids", t => t.BasketIds),
            Enum<CostSegment>("cost_segment", t => t.Trade.Costing?.Segment, nullable: true),
            Lng("cost_quantity", t => t.Trade.Costing?.Quantity, nullable: true),
            Dec("cost_risk_taken", t => t.Trade.Costing?.RiskTaken),
            Dec("cost_entry_turnover", t => t.Trade.Costing?.EntryTurnover),
            Dec("cost_exit_turnover", t => t.Trade.Costing?.ExitTurnover),
        };
        AddLeg(columns, "entry", t => t.Trade.Costing?.EntryLeg);
        AddLeg(columns, "exit", t => t.Trade.Costing?.ExitLeg);
        columns.Add(Dec("cost_total", t => t.Trade.Costing?.TotalCost));
        columns.Add(Dbl("cost_r", t => t.Trade.Costing?.CostR, nullable: true));
        columns.Add(Dbl("r_net", t => t.Trade.Costing?.RNet, nullable: true));
        return columns;
    }

    private static void AddLeg(List<TradeColumn> columns, string prefix, Func<RunTrade, LegCharges?> leg)
    {
        columns.Add(Dec(prefix + "_brokerage", t => leg(t)?.Brokerage));
        columns.Add(Dec(prefix + "_stt", t => leg(t)?.Stt));
        columns.Add(Dec(prefix + "_exchange_txn", t => leg(t)?.ExchangeTxn));
        columns.Add(Dec(prefix + "_sebi_fee", t => leg(t)?.SebiFee));
        columns.Add(Dec(prefix + "_stamp_duty", t => leg(t)?.StampDuty));
        columns.Add(Dec(prefix + "_other_charges", t => leg(t)?.OtherCharges));
        columns.Add(Dec(prefix + "_dp_charge", t => leg(t)?.DpCharge));
        columns.Add(Dec(prefix + "_gst", t => leg(t)?.Gst));
    }

    private static TradeColumn Str(string name, int length, Func<RunTrade, string> get) =>
        new(name, typeof(string), NpgsqlDbType.Varchar, $"character varying({length.ToString(CultureInfo.InvariantCulture)})", false, t => get(t), (r, i) => r.GetString(i), null);

    private static TradeColumn Txt(string name, Func<RunTrade, string?> get) =>
        new(name, typeof(string), NpgsqlDbType.Text, "text", true, t => get(t), (r, i) => r.IsDBNull(i) ? null : r.GetString(i), null);

    private static TradeColumn Uid(string name, Func<RunTrade, Guid> get) =>
        new(name, typeof(Guid), NpgsqlDbType.Uuid, "uuid", false, t => get(t), (r, i) => r.GetGuid(i), null);

    private static TradeColumn Uids(string name, Func<RunTrade, IReadOnlyList<Guid>> get) =>
        new(name, typeof(Guid[]), NpgsqlDbType.Array | NpgsqlDbType.Uuid, "uuid[]", false, t => get(t) is Guid[] array ? array : get(t).ToArray(), (r, i) => r.GetFieldValue<Guid[]>(i), null);

    private static TradeColumn Dat(string name, Func<RunTrade, DateOnly> get) =>
        new(name, typeof(DateOnly), NpgsqlDbType.Date, "date", false, t => get(t), (r, i) => r.GetFieldValue<DateOnly>(i), null);

    private static TradeColumn Lng(string name, Func<RunTrade, long> get) =>
        new(name, typeof(long), NpgsqlDbType.Bigint, "bigint", false, t => get(t), (r, i) => r.GetInt64(i), null);

    private static TradeColumn Lng(string name, Func<RunTrade, long?> get, bool nullable) =>
        new(name, typeof(long?), NpgsqlDbType.Bigint, "bigint", nullable, t => get(t), (r, i) => r.IsDBNull(i) ? null : r.GetInt64(i), null);

    private static TradeColumn Int(string name, Func<RunTrade, int> get) =>
        new(name, typeof(int), NpgsqlDbType.Integer, "integer", false, t => get(t), (r, i) => r.GetInt32(i), null);

    private static TradeColumn Bln(string name, Func<RunTrade, bool> get) =>
        new(name, typeof(bool), NpgsqlDbType.Boolean, "boolean", false, t => get(t), (r, i) => r.GetBoolean(i), null);

    private static TradeColumn Dbl(string name, Func<RunTrade, double> get) =>
        new(name, typeof(double), NpgsqlDbType.Double, "double precision", false, t => get(t), (r, i) => r.GetDouble(i), null);

    private static TradeColumn Dbl(string name, Func<RunTrade, double?> get, bool nullable) =>
        new(name, typeof(double?), NpgsqlDbType.Double, "double precision", nullable, t => get(t), (r, i) => r.IsDBNull(i) ? null : r.GetDouble(i), null);

    /// <summary>A costing amount: nullable (null when the trade is gross), <c>numeric</c> without a scale so that a decimal round-trips with its value.</summary>
    private static TradeColumn Dec(string name, Func<RunTrade, decimal?> get) =>
        new(name, typeof(decimal?), NpgsqlDbType.Numeric, "numeric", true, t => get(t), (r, i) => r.IsDBNull(i) ? null : r.GetDecimal(i), null);

    private static TradeColumn Enum<T>(string name, Func<RunTrade, T> get)
        where T : struct, Enum =>
        new(name, typeof(string), NpgsqlDbType.Varchar, "character varying(16)", false, t => EnumText<T>.Name(get(t)), (r, i) => EnumText<T>.Parse(r.GetString(i)), EnumText<T>.Names);

    private static TradeColumn Enum<T>(string name, Func<RunTrade, T?> get, bool nullable)
        where T : struct, Enum =>
        new(name, typeof(string), NpgsqlDbType.Varchar, "character varying(16)", nullable, t => get(t) is { } value ? EnumText<T>.Name(value) : null, (r, i) => r.IsDBNull(i) ? null : EnumText<T>.Parse(r.GetString(i)), EnumText<T>.Names);
}
