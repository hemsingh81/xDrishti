using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns Dhan's ledger (ADR 0025, <c>GET /v2/ledger</c>) into lines; the answer is a list of rows, the list under <c>data</c> or - as in Dhan's published sample - one row as a single object. A row is read by <c>voucherdate</c>, <c>voucherdesc</c>, <c>narration</c>, <c>vouchernumber</c>, <c>exchange</c>, <c>debit</c>, <c>credit</c> and <c>runbal</c> (names in any case; numbers as
/// JSON numbers or text such as <c>"20,000.00"</c>; dates as <c>Jun 22, 2022</c>, <c>2022-06-22</c> and similar). The amount is one signed number, credit minus debit. A row whose date or an amount that is present does not read, a row with neither
/// debit nor credit, and a row that moves nothing (an opening-balance line) is dropped and counted <b>with its reason</b> (<see cref="BrokerDropReasons"/>: the first problem in the order date, amounts present, amounts readable, non-zero) and the index of the first few; nothing of the row's content is kept. Texts are Dhan's free text: kept as they are here (the store cleans and caps them). Dhan's field names are from its public documentation, not from a
/// real answer of the owner's account. Pure.
/// </summary>
internal static class DhanLedgerMapper
{
    /// <summary>The version of this mapping (ADR 0027): raise it with every change that lets a row be read that was dropped before; see <see cref="DhanTradeMapper.Version"/>. 1: the mapping of ADR 0025 amendment g.</summary>
    public const int Version = 1;

    private const decimal Limit = 1_000_000_000_000m;

    public static BrokerLedgerRead Map(JsonElement rows)
    {
        if (SingleRow(rows) is { } single)
        {
            // The published ledger sample is one object, not a list: an answer of one row is read as one.
            var only = Read(single);
            return only.Line is { } line ? new BrokerLedgerRead([line], 1, 0) : new BrokerLedgerRead([], 1, 1) { Drops = BrokerReadDrops.From([new BrokerRowDrop(0, only.Drop ?? BrokerDropReasons.Other)]) };
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return BrokerLedgerRead.Empty;
        }

        var lines = new List<BrokerLedgerRow>(rows.GetArrayLength());
        var drops = new List<BrokerRowDrop>();
        var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var result = Read(row);
            if (result.Line is { } line)
            {
                lines.Add(line);
            }
            else
            {
                drops.Add(new BrokerRowDrop(index, result.Drop ?? BrokerDropReasons.Other));
            }

            index++;
        }

        return new BrokerLedgerRead(lines, index, drops.Count) { Drops = BrokerReadDrops.From(drops) };
    }

    private static readonly string[] RowFields = ["voucherdate", "voucherdesc", "vouchernumber", "narration", "debit", "credit", "runbal"];

    /// <summary>The ledger row an answer is when it is one object (or one object under <c>data</c>) that has ledger fields; null for a list, an error body or anything else.</summary>
    public static JsonElement? SingleRow(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return SingleRow(data);
        }

        return body.EnumerateObject().Any(p => RowFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) ? body : null;
    }

    /// <summary>A ledger line, or the <see cref="BrokerDropReasons"/> reason why the row is not one. Exactly one is set.</summary>
    private readonly record struct RowResult(BrokerLedgerRow? Line, string? Drop)
    {
        public static RowResult Dropped(string reason) => new(null, reason);
    }

    private static RowResult Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return RowResult.Dropped(BrokerDropReasons.UnreadableRow);
        }

        // The first problem found is the reason; the order is the order of the checks.
        if (DhanJson.Date(DhanJson.Text(row, "voucherdate", "voucherDate", "date")) is not { } voucherDate)
        {
            return RowResult.Dropped(BrokerDropReasons.BadDate);
        }

        var debit = Amount(row, "debit");
        var credit = Amount(row, "credit");
        if (debit is null && credit is null)
        {
            return RowResult.Dropped(BrokerDropReasons.MissingAmount);
        }

        if (debit is { IsBad: true } || credit is { IsBad: true })
        {
            return RowResult.Dropped(BrokerDropReasons.BadAmount);
        }

        var amount = (credit?.Value ?? 0m) - (debit?.Value ?? 0m);
        if (amount == 0m)
        {
            return RowResult.Dropped(BrokerDropReasons.ZeroAmount);
        }

        var balance = Amount(row, "runbal", "runningBalance");
        return new RowResult(
            new BrokerLedgerRow(
                voucherDate,
                DhanJson.Text(row, "vouchernumber", "voucherNumber", "voucherNo") ?? string.Empty,
                DhanJson.Text(row, "voucherdesc", "voucherDesc", "description") ?? string.Empty,
                DhanJson.Text(row, "narration") ?? string.Empty,
                DhanJson.Text(row, "exchange"),
                amount,
                balance is { IsBad: false } ? balance.Value.Value : (decimal?)null),
            null);
    }

    private readonly record struct Money(decimal Value, bool IsBad);

    /// <summary>The amount in the first field of the names that is present; null when none is (or it is blank); bad when it is present and is not a plausible number.</summary>
    private static Money? Amount(JsonElement row, params string[] names)
    {
        var text = DhanJson.Text(row, names);
        if (text is null)
        {
            return null;
        }

        return decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) && Math.Abs(number) <= Limit
            ? new Money(number, false)
            : new Money(0m, true);
    }
}
