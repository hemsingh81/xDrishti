using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// The owner-run probe of the backfill (P14-03 criterion 9): a handful of real requests - one window of NIFTY WEEK 1 at ATM+10, NIFTY WEEK 2 at ATM+10, BANKNIFTY WEEK 1 in 2024-06, BANKNIFTY WEEK 1 in 2025-01 (after its weekly era), and one share at ATM+3 - and a printed account of what Dhan
/// returned: the HTTP status, Dhan's code, the keys and array lengths, the timestamp as sent and as read in India time, the strike against the spot, the IV range and the bars the parser keeps. <b>It stores nothing</b> (it holds no store) and never
/// prints a token, a client id or a URL. At most five requests, at the backfill's spacing; a refused token or a missing plan ends it. The owner reads the lines, records them in ADR 0177 and only then accepts the result.
/// </summary>
public sealed class BackfillProbe(IOptionBackfillClient client, OptionBackfillSettings settings)
{
    /// <summary>The most requests a probe makes.</summary>
    public const int MaxRequests = 5;

    public async Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<BackfillTarget> targets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var lines = new List<string> { "Backfill probe: at most 5 requests, nothing is stored. Every shape below is what Dhan returned; it is UNVERIFIED until you record it in ADR 0177." };
        var recent = BackfillPlanner.Windows(IndiaTime.Today(now), 1)[0];
        var nifty = targets.FirstOrDefault(t => t.IsIndex && t.Symbol == BackfillPlanner.Nifty);
        var bank = targets.FirstOrDefault(t => t.IsIndex && t.Symbol == BackfillPlanner.BankNifty);
        var stock = targets.FirstOrDefault(t => !t.IsIndex);
        var cases = new List<(string Label, BackfillItem? Item)>
        {
            ("NIFTY WEEK 1 at ATM+10, last 30 days", Item(nifty, "OPTIDX", 5, 'W', 1, 10, recent)),
            ("NIFTY WEEK 2 at ATM+10, last 30 days", Item(nifty, "OPTIDX", 5, 'W', 2, 10, recent)),
            ("BANKNIFTY WEEK 1 at ATM in 2024-06 (inside its weekly era)", Item(bank, "OPTIDX", 5, 'W', 1, 0, new DateOnly(2024, 6, 1))),
            ("BANKNIFTY WEEK 1 at ATM in 2025-01 (after its weekly era: empty or DH-907?)", Item(bank, "OPTIDX", 5, 'W', 1, 0, new DateOnly(2025, 1, 1))),
            ("one share MONTH 1 at ATM+3, last 30 days", Item(stock, "OPTSTK", 15, 'M', 1, 3, recent)),
        };
        foreach (var (label, item) in cases)
        {
            lines.Add(string.Empty);
            lines.Add("== " + label);
            if (item is null)
            {
                lines.Add("   skipped: no such underlying in the option underlyings with a Dhan id");
                continue;
            }

            var reply = await client.SendAsync(item.ToRequest(), settings.Spacing, cancellationToken);
            if (reply.NoCredentials)
            {
                lines.Add("   stopped: the Data account has no usable token");
                break;
            }

            lines.Add($"   outcome={reply.Outcome} http={reply.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"} dhan_code={reply.DhanCode ?? "-"} latency_ms={reply.LatencyMs.ToString(CultureInfo.InvariantCulture)}");
            if (reply.Body is { } body)
            {
                Describe(body, item, lines);
            }

            if (reply.Outcome is BackfillOutcomes.TokenRefused or BackfillOutcomes.NotSubscribed)
            {
                lines.Add("   stopped: Dhan refused the token or the plan");
                break;
            }
        }

        lines.Add(string.Empty);
        lines.Add("Record in ADR 0177: whether code 2 accepts +-10, the timestamp zone (first bar should read 09:15 India time), the IV unit, empty versus DH-907 for the out-of-era window, and bars per window.");
        return lines;
    }

    private static BackfillItem? Item(BackfillTarget? target, string instrument, int interval, char flag, int code, int offset, DateOnly from) =>
        target is null ? null : new BackfillItem(new BackfillKey(target.InstrumentId, flag, code, offset, "CE", interval, from), target.Scrip, instrument, target.StrikeStepPaise);

    private static void Describe(byte[] body, BackfillItem item, List<string> lines)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            lines.Add("   top-level keys: " + Keys(root));
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                lines.Add("   data keys: " + Keys(data));
                foreach (var side in new[] { "ce", "pe" })
                {
                    if (!data.TryGetProperty(side, out var own) || own.ValueKind != JsonValueKind.Object)
                    {
                        lines.Add($"   data.{side}: {(data.TryGetProperty(side, out _) ? "null" : "absent")}");
                        continue;
                    }

                    lines.Add($"   data.{side} arrays (length): " + string.Join(", ", own.EnumerateObject().Select(p => $"{p.Name}({(p.Value.ValueKind == JsonValueKind.Array ? p.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture) : p.Value.ValueKind.ToString())})")));
                    if (own.TryGetProperty("timestamp", out var times) && times.ValueKind == JsonValueKind.Array && times.GetArrayLength() > 0)
                    {
                        lines.Add($"   first timestamp as sent: {times[0].GetRawText()}  last: {times[times.GetArrayLength() - 1].GetRawText()}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            lines.Add("   the body is not JSON");
            return;
        }

        var parsed = RollingBarParser.Parse(body, item);
        lines.Add($"   parser: outcome={parsed.Outcome} kept={parsed.Bars.Count.ToString(CultureInfo.InvariantCulture)} outside_hours={parsed.OutsideHours.ToString(CultureInfo.InvariantCulture)} unusable={parsed.Unusable.ToString(CultureInfo.InvariantCulture)} suspect={parsed.Bars.Count(b => (b.Flags & BackfillFlags.Suspect) != 0).ToString(CultureInfo.InvariantCulture)}");
        if (parsed.Bars.Count > 0)
        {
            var first = parsed.Bars[0];
            var ist = IndiaTime.ToIst(first.Ts);
            lines.Add($"   first kept bar: {ist:yyyy-MM-dd HH:mm} India time (read as Unix time), strike {first.StrikePx / 100m:0.##}, spot {first.SpotPx / 100m:0.##}, close {first.ClosePx / 100m:0.##}");
            var ivs = parsed.Bars.Where(b => b.Iv is not null).Select(b => b.Iv!.Value).ToList();
            lines.Add(ivs.Count > 0 ? $"   iv as sent: min {ivs.Min().ToString("0.####", CultureInfo.InvariantCulture)} max {ivs.Max().ToString("0.####", CultureInfo.InvariantCulture)} (percent or fraction? decide from this)" : "   iv: none sent");
            lines.Add($"   distinct strikes in the window: {parsed.Bars.Select(b => b.StrikePx).Distinct().Count().ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static string Keys(JsonElement element) => element.ValueKind == JsonValueKind.Object ? string.Join(", ", element.EnumerateObject().Select(p => p.Name)) : element.ValueKind.ToString();
}
