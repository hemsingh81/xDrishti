using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Turns Dhan's ledger (ADR 0025, <c>GET /v2/ledger</c>) into lines. A row is read by <c>voucherdate</c>, <c>voucherdesc</c>, <c>narration</c>, <c>vouchernumber</c>, <c>exchange</c>, <c>debit</c>, <c>credit</c> and <c>runbal</c> (names in any case; numbers as
/// JSON numbers or text such as <c>"20,000.00"</c>; dates as <c>Jun 22, 2022</c>, <c>2022-06-22</c> and similar). The amount is one signed number, credit minus debit. A row whose date or an amount that is present does not read, a row with neither
/// debit nor credit, and a row that moves nothing (an opening-balance line) is dropped and counted. Texts are Dhan's free text: kept as they are here (the store cleans and caps them). Dhan's field names are from its public documentation, not from a
/// real answer of the owner's account. Pure.
/// </summary>
internal static class DhanLedgerMapper
{
    private const decimal Limit = 1_000_000_000_000m;

    public static BrokerLedgerRead Map(JsonElement rows)
    {
        if (rows.ValueKind != JsonValueKind.Array)
        {
            return BrokerLedgerRead.Empty;
        }

        var lines = new List<BrokerLedgerRow>(rows.GetArrayLength());
        var received = 0;
        foreach (var row in rows.EnumerateArray())
        {
            received++;
            if (Read(row) is { } line)
            {
                lines.Add(line);
            }
        }

        return new BrokerLedgerRead(lines, received, received - lines.Count);
    }

    private static BrokerLedgerRow? Read(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var date = DhanJson.Date(DhanJson.Text(row, "voucherdate", "voucherDate", "date"));
        var debit = Amount(row, "debit");
        var credit = Amount(row, "credit");
        if (date is not { } voucherDate || (debit is null && credit is null) || debit is { IsBad: true } || credit is { IsBad: true })
        {
            return null;
        }

        var amount = (credit?.Value ?? 0m) - (debit?.Value ?? 0m);
        if (amount == 0m)
        {
            return null;
        }

        var balance = Amount(row, "runbal", "runningBalance");
        return new BrokerLedgerRow(
            voucherDate,
            DhanJson.Text(row, "vouchernumber", "voucherNumber", "voucherNo") ?? string.Empty,
            DhanJson.Text(row, "voucherdesc", "voucherDesc", "description") ?? string.Empty,
            DhanJson.Text(row, "narration") ?? string.Empty,
            DhanJson.Text(row, "exchange"),
            amount,
            balance is { IsBad: false } ? balance.Value.Value : (decimal?)null);
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
