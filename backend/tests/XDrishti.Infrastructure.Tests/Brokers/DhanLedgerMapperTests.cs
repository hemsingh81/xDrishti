using System.Text.Json;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>Dhan's ledger rows into lines with one signed amount (ADR 0025, P2-13 criterion 3): the committed sample, dates and numbers in several shapes, rows that do not read.</summary>
public sealed class DhanLedgerMapperTests
{
    private static BrokerLedgerRead Map(string json) => DhanLedgerMapper.Map(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void The_sample_gives_the_lines_with_credit_minus_debit_and_counts_the_unusable_rows()
    {
        var read = DhanLedgerMapper.Map(DhanSamples.Json("dhan-ledger.json"));

        (read.Received, read.Dropped).ShouldBe((8, 4)); // the opening balance moves nothing; a bad amount, a bad date and a row without amounts
        read.Rows.ShouldBe(
        [
            new BrokerLedgerRow(new DateOnly(2026, 9, 2), "202600036701", "RECEIPT", "FUNDS DEPOSIT THROUGH UPI", "NSE-CAPITAL", 25000m, 25957.29m),
            new BrokerLedgerRow(new DateOnly(2026, 9, 2), "202600036702", "BILL", "BILL FOR 02-SEP-2026 NSE-CAPITAL EQUITY PURCHASE", "NSE-CAPITAL", -905m, 25052.29m),
            new BrokerLedgerRow(new DateOnly(2026, 9, 20), "202600036703", "PAYBLE", "FUNDS WITHDRAWAL", "NSE-CAPITAL", -10000m, 15052.29m),
            new BrokerLedgerRow(new DateOnly(2026, 9, 21), "202600036704", "JOURNAL", "DIVIDEND CREDIT EXMPL FINAL DIV", "NSE-CAPITAL", 1500m, 16552.29m),
        ]);
    }

    [Fact]
    public void Dhans_published_sample_is_one_object_and_maps_as_one_debit_with_its_voucher_description()
    {
        var read = DhanLedgerMapper.Map(DhanSamples.Json("dhan-published-ledger.json"));

        (read.Received, read.Dropped).ShouldBe((1, 0));
        read.Rows.ShouldBe([new BrokerLedgerRow(new DateOnly(2022, 6, 22), "202200036701", "PAYBNK", "FUNDS WITHDRAWAL", "NSE-CAPITAL", -20000m, 957.29m)]);
    }

    [Fact]
    public void A_single_object_that_does_not_read_is_one_dropped_row()
    {
        var read = Map("""{"narration":"X","voucherdate":"not a date","debit":"1.00","credit":"0.00"}""");

        (read.Received, read.Dropped, read.Rows.Count).ShouldBe((1, 1, 0));
    }

    [Theory]
    [InlineData("Jun 22, 2022", "2022-06-22")]
    [InlineData("Jun 2, 2022", "2022-06-02")]
    [InlineData("2022-06-22", "2022-06-22")]
    [InlineData("22/06/2022", "2022-06-22")]
    [InlineData("22-Jun-2022", "2022-06-22")]
    [InlineData("2022-06-22 00:00:00", "2022-06-22")]
    [InlineData("2022-06-22T00:00:00", "2022-06-22")]
    public void The_date_reads_in_the_shapes_of_dhans_documents(string text, string expected)
    {
        var read = Map($$"""[{"voucherdate":"{{text}}","voucherdesc":"X","vouchernumber":"1","debit":"1","credit":"0"}]""");

        read.Rows.ShouldHaveSingleItem().Date.ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_debit_alone_a_credit_alone_and_both_are_one_signed_amount()
    {
        var read = Map("""
            [{"voucherdate":"2026-09-01","vouchernumber":"1","debit":"10.50"},
             {"voucherdate":"2026-09-01","vouchernumber":"2","credit":"7"},
             {"voucherdate":"2026-09-01","vouchernumber":"3","debit":"2","credit":"5.25"}]
            """);

        read.Rows.Select(r => r.Amount).ShouldBe([-10.5m, 7m, 3.25m]);
    }

    [Fact]
    public void Missing_texts_are_empty_and_a_missing_running_balance_is_none()
    {
        var line = Map("""[{"voucherdate":"2026-09-01","credit":"7"}]""").Rows.Single();

        (line.VoucherNo, line.Description, line.Narration, line.Exchange, line.RunningBalance).ShouldBe((string.Empty, string.Empty, string.Empty, null, null));
    }

    [Fact]
    public void An_absurd_amount_drops_the_row()
    {
        var read = Map("""[{"voucherdate":"2026-09-01","credit":"1e40"}]""");

        (read.Rows.Count, read.Dropped).ShouldBe((0, 1));
    }

    [Fact]
    public void A_running_balance_that_does_not_read_is_left_out_but_the_line_stays()
    {
        var line = Map("""[{"voucherdate":"2026-09-01","credit":"7","runbal":"n/a"}]""").Rows.Single();

        line.RunningBalance.ShouldBeNull();
    }

    [Fact]
    public void Something_that_is_not_an_array_has_no_rows()
    {
        Map("""{"x":1}""").ShouldBe(BrokerLedgerRead.Empty);
    }
}
