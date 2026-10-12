using XDrishti.Application.Market.Bars.Csv;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class CsvFileNameTests
{
    [Theory]
    [InlineData("ABB_1m.csv.gz", "ABB")]
    [InlineData("ABB_1m.csv", "ABB")]
    [InlineData("M&M_1m.csv.gz", "M&M")]
    [InlineData("BAJAJ-AUTO_1m.csv.gz", "BAJAJ-AUTO")]
    [InlineData("360ONE_1m.csv.gz", "360ONE")]
    [InlineData("abb_1m.csv", "abb")]
    public void Matching_names_give_the_symbol_as_written(string name, string symbol)
    {
        CsvFileName.TryParse(name, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(symbol);
    }

    [Theory]
    [InlineData("ABB_5m.csv.gz")]
    [InlineData("ABB_1m.csv.gz.tmp")]
    [InlineData("ABB_1m.CSV.GZ")]
    [InlineData("_1m.csv")]
    [InlineData("A B_1m.csv")]
    [InlineData("ABB_1m.csv\n")]
    [InlineData("../ABB_1m.csv")]
    [InlineData("ABB.csv")]
    [InlineData("notes.txt")]
    public void Other_names_are_not_data_files(string name)
    {
        CsvFileName.TryParse(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_master_list_symbol_is_the_exchange_and_the_symbol()
    {
        CsvFileName.Canonical("M&M").ShouldBe("NSE:M&M");
        CsvFileName.IsGzip("A_1m.csv.gz").ShouldBeTrue();
        CsvFileName.IsGzip("A_1m.csv").ShouldBeFalse();
    }
}
