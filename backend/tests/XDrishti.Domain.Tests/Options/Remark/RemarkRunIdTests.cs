using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 criterion 9 (identity): the same inputs in any order give the same run id; any one input changing gives another.</summary>
public sealed class RemarkRunIdTests
{
    private static readonly string Run = new('a', 64);
    private static readonly string Profile = new('b', 64);

    private static string Id(long watermark = 7, string usable = "u", string[]? spread = null, string sheet = "gross", string lots = "l", string epochs = "e", string contracts = "c", string settings = "s") =>
        RemarkRunId.Of(Run, Profile, watermark, usable, spread ?? ["x:1", "y:2"], sheet, lots, epochs, contracts, settings);

    [Fact]
    public void The_same_inputs_give_the_same_id_whatever_the_order_of_the_spread_inputs()
    {
        Id().ShouldBe(Id());
        Id(spread: ["y:2", "x:1"]).ShouldBe(Id(spread: ["x:1", "y:2"]));
        Id().ShouldMatch("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(8, "u", "gross", "l", "e", "c", "s")]
    [InlineData(7, "v", "gross", "l", "e", "c", "s")]
    [InlineData(7, "u", "abc", "l", "e", "c", "s")]
    [InlineData(7, "u", "gross", "m", "e", "c", "s")]
    [InlineData(7, "u", "gross", "l", "f", "c", "s")]
    [InlineData(7, "u", "gross", "l", "e", "d", "s")]
    [InlineData(7, "u", "gross", "l", "e", "c", "t")]
    public void Any_one_input_changing_gives_another_id(long watermark, string usable, string sheet, string lots, string epochs, string contracts, string settings) =>
        Id(watermark, usable, null, sheet, lots, epochs, contracts, settings).ShouldNotBe(Id());

    [Fact]
    public void The_hashes_of_the_rows_read_do_not_depend_on_the_order_they_were_read_in()
    {
        var u = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
        LotSizeEntry[] lots = [new(1, u, new DateOnly(2024, 11, 20), 75, "secondary", false), new(2, u, new DateOnly(2025, 12, 30), 65, "secondary", false)];
        RemarkRunId.LotsSha(lots).ShouldBe(RemarkRunId.LotsSha(lots.Reverse()));
        RemarkRunId.LotsSha(lots).ShouldNotBe(RemarkRunId.LotsSha(lots.Take(1)));
        RemarkRunId.UsableSha(["b", "a"]).ShouldBe(RemarkRunId.UsableSha(["a", "b"]));
        RemarkRunId.ContractsSha([new ContractExpiry(new DateOnly(2026, 11, 5), 'W'), new ContractExpiry(new DateOnly(2026, 11, 12), 'W')])
            .ShouldBe(RemarkRunId.ContractsSha([new ContractExpiry(new DateOnly(2026, 11, 12), 'W'), new ContractExpiry(new DateOnly(2026, 11, 5), 'W')]));
        RemarkRunId.EpochsSha([new MarketEpoch("a", new DateOnly(2025, 1, 1), "d", null, false)]).ShouldNotBe(RemarkRunId.EpochsSha([new MarketEpoch("a", new DateOnly(2025, 1, 2), "d", null, false)]));
    }
}
