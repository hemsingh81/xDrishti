using XDrishti.Application.Market.Import;

namespace XDrishti.Application.Tests.Market;

public sealed class SectorListTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sample_maps_ISINs_to_industries()
    {
        await using var stream = MasterFixtures.Open(MasterFixtures.Sectors);

        var result = await SectorList.ParseAsync(stream, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(26);
        result.Value.IndustryOf("INE002A01018").ShouldBe("Oil Gas & Consumable Fuels");
        result.Value.IndustryOf("INE000000000").ShouldBe("Services");
        result.Value.IndustryOf("INE999Z99999").ShouldBeNull();
        result.Value.IndustryOf(null).ShouldBeNull();
    }

    [Fact]
    public async Task A_changed_header_is_refused()
    {
        await using var stream = MasterFixtures.AsStream("Company,Sector,Ticker\nA,B,C\n");

        var result = await SectorList.ParseAsync(stream, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SectorList.FailureCode);
    }

    [Fact]
    public async Task A_list_without_rows_is_refused()
    {
        await using var stream = MasterFixtures.AsStream("Company Name,Industry,Symbol,Series,ISIN Code\n");

        (await SectorList.ParseAsync(stream, Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Short_rows_are_skipped_and_the_first_industry_of_an_ISIN_wins()
    {
        await using var stream = MasterFixtures.AsStream("Company Name,Industry,Symbol,Series,ISIN Code\nA,One,A,EQ,INE1\nbroken\nA again,Two,A,EQ,INE1\n");

        var result = await SectorList.ParseAsync(stream, Ct);

        result.Value.Count.ShouldBe(1);
        result.Value.IndustryOf("INE1").ShouldBe("One");
    }
}
