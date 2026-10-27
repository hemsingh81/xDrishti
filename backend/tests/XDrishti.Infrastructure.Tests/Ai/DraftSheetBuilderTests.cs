using XDrishti.Indicators;
using XDrishti.Infrastructure.Ai.Assistant;

namespace XDrishti.Infrastructure.Tests.Ai;

/// <summary>Spec P9-07 AC 12 and D3: the sheet is generated from code, so it is byte-identical across runs, holds every feature name of the catalog (a new indicator forces a review) and the two examples, and stays small.</summary>
public sealed class DraftSheetBuilderTests
{
    [Fact]
    public void The_sheet_is_byte_identical_across_builds_and_its_hash_is_stable()
    {
        var a = new DraftSheetBuilder();
        var b = new DraftSheetBuilder();

        a.Sheet.ShouldBe(b.Sheet);
        a.Hash.ShouldBe(b.Hash);
        a.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void The_sheet_names_every_feature_of_the_catalog()
    {
        var sheet = new DraftSheetBuilder().Sheet;

        foreach (var info in IndicatorCatalog.All)
        {
            sheet.ShouldContain("- " + info.Name, customMessage: "a new indicator needs a review of the draft sheet: " + info.Name);
        }
    }

    [Fact]
    public void The_sheet_holds_the_two_examples_the_gaps_and_is_under_12_KB()
    {
        var sheet = new DraftSheetBuilder().Sheet;

        sheet.ShouldContain("EXAMPLE VWAP_PULLBACK");
        sheet.ShouldContain("EXAMPLE BASE_BREAKOUT");
        sheet.ShouldContain("NO automatic mirror");
        sheet.ShouldContain("sector relative strength");
        System.Text.Encoding.UTF8.GetByteCount(sheet).ShouldBeLessThan(12 * 1024);
    }
}
