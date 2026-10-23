using Microsoft.Extensions.Configuration;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P7-13: unset means gross; a bad capital means the default.</summary>
public sealed class GradeSettingsTests
{
    private static GradeSettings Of(params (string Key, string? Value)[] data) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(data.Select(d => new KeyValuePair<string, string?>(d.Key, d.Value))).Build());

    [Fact]
    public void Unset_is_gross_with_the_default_capital()
    {
        var settings = Of();

        settings.CostSheetId.ShouldBeNull();
        settings.Capital.ShouldBe(1_000_000m);
    }

    [Fact]
    public void A_sheet_and_a_capital_are_read_and_a_bad_capital_falls_back()
    {
        Of(("Planning:Grading:CostSheetId", " nse-eq "), ("Planning:Grading:Capital", "250000")).CostSheetId.ShouldBe("nse-eq");
        Of(("Planning:Grading:Capital", "250000")).Capital.ShouldBe(250000m);
        Of(("Planning:Grading:Capital", "-5")).Capital.ShouldBe(1_000_000m);
        Of(("Planning:Grading:CostSheetId", "  ")).CostSheetId.ShouldBeNull();
    }
}
