using XDrishti.Application.Market.Search;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class SearchInstrumentsHandlerTests
{
    private static InstrumentSearchRequest Request(string? q = null, string? type = null, string? sort = null, int? page = null, int? pageSize = null) =>
        new(q, type, null, null, null, null, null, sort, page, pageSize);

    [Fact]
    public void The_defaults_are_the_first_page_of_fifty_sorted_by_symbol_without_retired()
    {
        var result = SearchInstrumentsHandler.Check(Request());

        var search = result.Value;
        (search.Page, search.PageSize, search.SortField, search.Descending, search.IncludeRetired).ShouldBe((1, 50, InstrumentSortField.Symbol, false, false));
        search.Text.ShouldBeNull();
    }

    [Fact]
    public void Text_and_filters_are_trimmed_and_blank_means_no_filter()
    {
        var result = SearchInstrumentsHandler.Check(new InstrumentSearchRequest("  rel  ", " share ", " Metals & Mining ", true, false, " EQ ", true, null, null, null));

        var search = result.Value;
        (search.Text, search.Type, search.Sector, search.Fno, search.AsmGsm, search.Series, search.IncludeRetired)
            .ShouldBe(("rel", InstrumentType.Share, "Metals & Mining", true, false, "EQ", true));
        SearchInstrumentsHandler.Check(new InstrumentSearchRequest(" ", " ", " ", null, null, " ", null, " ", null, null)).Value.Text.ShouldBeNull();
    }

    [Theory]
    [InlineData("symbol", InstrumentSortField.Symbol, false)]
    [InlineData("-symbol", InstrumentSortField.Symbol, true)]
    [InlineData("Name", InstrumentSortField.Name, false)]
    [InlineData("-lot", InstrumentSortField.Lot, true)]
    [InlineData("tick", InstrumentSortField.Tick, false)]
    [InlineData("-sector", InstrumentSortField.Sector, true)]
    public void Sorting_accepts_the_documented_fields_with_an_optional_minus(string sort, InstrumentSortField field, bool descending)
    {
        var search = SearchInstrumentsHandler.Check(Request(sort: sort)).Value;

        (search.SortField, search.Descending).ShouldBe((field, descending));
    }

    [Theory]
    [InlineData("pageSize", 0)]
    [InlineData("pageSize", 101)]
    [InlineData("pageSize", -5)]
    [InlineData("page", 0)]
    [InlineData("page", -1)]
    public void Numbers_outside_their_range_are_rejected_naming_the_field(string field, int value)
    {
        var result = SearchInstrumentsHandler.Check(field == "page" ? Request(page: value) : Request(pageSize: value));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("instruments.invalid");
        result.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.Validation);
        result.Error.Fields!.ShouldContain(f => f.Field == field);
    }

    [Fact]
    public void The_deepest_page_is_one_hundred_thousand()
    {
        SearchInstrumentsHandler.Check(Request(page: 100_000)).IsSuccess.ShouldBeTrue();

        var result = SearchInstrumentsHandler.Check(Request(page: 100_001));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("instruments.invalid");
        result.Error.Fields!.ShouldContain(f => f.Field == "page");
        SearchInstrumentsHandler.Check(Request(page: int.MaxValue)).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("q")]
    [InlineData("sector")]
    [InlineData("series")]
    public void Control_characters_are_rejected_in_the_text_filters(string field)
    {
        foreach (var bad in new[] { "a\0b", "tab\there", "line\nbreak", "\u001b[31m", "\u007f" })
        {
            var request = field switch
            {
                "q" => Request(q: bad),
                "sector" => new InstrumentSearchRequest(null, null, bad, null, null, null, null, null, null, null),
                _ => new InstrumentSearchRequest(null, null, null, null, null, bad, null, null, null, null),
            };

            var result = SearchInstrumentsHandler.Check(request);

            result.IsFailure.ShouldBeTrue(bad);
            result.Error.Fields!.ShouldContain(f => f.Field == field);
        }
    }

    [Theory]
    [InlineData("sector", 100, true)]
    [InlineData("sector", 101, false)]
    [InlineData("series", 8, true)]
    [InlineData("series", 9, false)]
    public void The_text_filters_have_length_caps(string field, int length, bool accepted)
    {
        var value = new string('x', length);
        var request = field == "sector"
            ? new InstrumentSearchRequest(null, null, value, null, null, null, null, null, null, null)
            : new InstrumentSearchRequest(null, null, null, null, null, value, null, null, null, null);

        SearchInstrumentsHandler.Check(request).IsSuccess.ShouldBe(accepted);
    }

    [Fact]
    public void Ordinary_text_with_symbols_and_spaces_is_accepted() =>
        SearchInstrumentsHandler.Check(Request(q: "M&M Fin. (ltd) 100%_ \\")).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Page_sizes_up_to_one_hundred_are_accepted() =>
        SearchInstrumentsHandler.Check(Request(pageSize: 100)).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData("type", "bond")]
    [InlineData("sort", "price")]
    [InlineData("sort", "-")]
    [InlineData("sort", "3")]
    public void An_unknown_type_or_sort_is_rejected(string field, string value)
    {
        var result = SearchInstrumentsHandler.Check(field == "type" ? Request(type: value) : Request(sort: value));

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.ShouldContain(f => f.Field == field);
    }

    [Fact]
    public void Over_long_text_is_rejected_and_all_problems_are_reported_together()
    {
        var result = SearchInstrumentsHandler.Check(Request(q: new string('x', 101), type: "bond", pageSize: 500));

        result.Error.Fields!.Select(f => f.Field).Order().ShouldBe(["pageSize", "q", "type"]);
    }
}
