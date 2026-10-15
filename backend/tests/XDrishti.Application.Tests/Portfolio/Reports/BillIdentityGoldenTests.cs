using XDrishti.Application.Portfolio.Reports;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// Pins the identity of a bill-level trade. It hashes the bill number, the day, the key of the scrip's name (<c>DividendNames</c>) and the side, so a change of the name rules would make the same report import again as new trades. If this fails because the rules were
/// changed on purpose, the identities of stored trades need a migration first.
/// </summary>
public sealed class BillIdentityGoldenTests
{
    private static string Id(string name, string bill, TradeSide side) =>
        BillTrades.Identity(new GtrRow(2, new DateOnly(2026, 4, 7), name, "NSE", bill, 1, 10m, 0, 0m, 0m), side, BillName.Parse(name));

    [Fact]
    public void The_identities_of_known_rows_do_not_change()
    {
        Id("Orion Gold ETF (ORIONGLD)", "443400", TradeSide.Buy).ShouldBe("g:da2b9628bd2d7093a898c013faf926e4");
        Id("ORION STEEL LTD.", "443401", TradeSide.Sell).ShouldBe("g:2df2234241f31b1f08b7856816a7e306");
    }

    [Fact]
    public void The_exchange_is_not_part_of_the_identity_unless_a_collision_salts_it()
    {
        var row = new GtrRow(2, new DateOnly(2026, 4, 7), "ORION STEEL LTD.", "BSE", "443401", 1, 10m, 0, 0m, 0m);
        var name = BillName.Parse(row.Name);

        BillTrades.Identity(row, TradeSide.Sell, name).ShouldBe("g:2df2234241f31b1f08b7856816a7e306");
        BillTrades.Identity(row, TradeSide.Sell, name, "BSE").ShouldNotBe(BillTrades.Identity(row, TradeSide.Sell, name));
    }

    [Theory]
    [InlineData("NSE", true, "nse")]
    [InlineData(" nse ", true, "nse")]
    [InlineData("BSE", false, "bse")]
    [InlineData("", false, "unknown")]
    public void Only_an_nse_row_keeps_the_plain_identity_and_a_blank_exchange_gets_a_fixed_token(string exchange, bool keeps, string salt)
    {
        var row = new GtrRow(2, new DateOnly(2026, 4, 7), "ORION STEEL LTD.", exchange, "443401", 1, 10m, 0, 0m, 0m);

        BillTrades.KeepsPlainIdentity(row).ShouldBe(keeps);
        BillTrades.CollisionSalt(row).ShouldBe(salt);
    }
}
