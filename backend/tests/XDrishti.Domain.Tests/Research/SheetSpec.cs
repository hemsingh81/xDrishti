using System.Text;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// A charge sheet as text, one raw token per field, so that a test can break exactly one thing: a field set to <c>null</c> (the C# null) is left out, one set to <c>"null"</c> is written as JSON null, one set to <c>"\"0.03\""</c> is a string.
/// The default is a valid sheet (the numbers of the TEST sheet, one schedule). <c>Extra</c> members are appended raw inside the object, which is how a repeated or an unknown name is written.
/// </summary>
internal sealed class SheetSpec
{
    public static readonly string[] RateNames = ["brokerage_pct", "brokerage_cap", "brokerage_min", "stt_buy_pct", "stt_sell_pct", "exchange_txn_pct", "sebi_per_crore", "stamp_buy_pct", "other_pct", "dp_charge_per_sell"];

    public string? Id { get; set; } = "\"test-spec-1\"";

    public string? Label { get; set; } = "\"Spec sheet\"";

    public string? Source { get; set; } = "\"invented\"";

    public string? Checked { get; set; }

    public string? Extra { get; set; }

    public List<ScheduleSpec> Schedules { get; } = [new ScheduleSpec("\"2026-01-01\"")];

    public string? SchedulesRaw { get; set; }

    public string Render()
    {
        var text = new StringBuilder("{");
        Member(text, "id", Id);
        Member(text, "label", Label);
        Member(text, "source", Source);
        Member(text, "checked", Checked);
        Member(text, "schedules", SchedulesRaw ?? "[" + string.Join(',', Schedules.Select(s => s.Render())) + "]");
        if (Extra is not null)
        {
            Separate(text);
            text.Append(Extra);
        }

        return text.Append('}').ToString();
    }

    public static void Member(StringBuilder text, string name, string? raw)
    {
        if (raw is null)
        {
            return;
        }

        Separate(text);
        text.Append('"').Append(name).Append("\":").Append(raw);
    }

    private static void Separate(StringBuilder text)
    {
        if (text[^1] != '{' && text[^1] != '[')
        {
            text.Append(',');
        }
    }

    /// <summary>One schedule.</summary>
    internal sealed class ScheduleSpec(string from)
    {
        public string? From { get; set; } = from;

        public string? Gst { get; set; } = "18";

        public string? Rounding { get; set; } = "\"half_away_from_zero\"";

        public string? Extra { get; set; }

        public Dictionary<string, string?> Intraday { get; } = Rates(IntradayValues);

        public Dictionary<string, string?> Delivery { get; } = Rates(DeliveryValues);

        public string? IntradayRaw { get; set; }

        public string? DeliveryRaw { get; set; }

        public string? IntradayExtra { get; set; }

        private static readonly string[] IntradayValues = ["0.03", "20", "0", "0", "0.04", "0.005", "50", "0.02", "0", "0"];

        private static readonly string[] DeliveryValues = ["0", "0", "0", "0.08", "0.08", "0.004", "50", "0.03", "0", "10"];

        public string Render()
        {
            var text = new StringBuilder("{");
            Member(text, "effective_from", From);
            Member(text, "gst_pct", Gst);
            Member(text, "rounding", Rounding);
            Member(text, "intraday_equity", IntradayRaw ?? RenderRates(Intraday, IntradayExtra));
            Member(text, "delivery_equity", DeliveryRaw ?? RenderRates(Delivery, null));
            if (Extra is not null)
            {
                Separate(text);
                text.Append(Extra);
            }

            return text.Append('}').ToString();
        }

        private static Dictionary<string, string?> Rates(string[] values)
        {
            var rates = new Dictionary<string, string?>();
            for (var i = 0; i < RateNames.Length; i++)
            {
                rates[RateNames[i]] = values[i];
            }

            return rates;
        }

        private static string RenderRates(Dictionary<string, string?> rates, string? extra)
        {
            var text = new StringBuilder("{");
            foreach (var name in RateNames)
            {
                Member(text, name, rates[name]);
            }

            if (extra is not null)
            {
                Separate(text);
                text.Append(extra);
            }

            return text.Append('}').ToString();
        }
    }
}
