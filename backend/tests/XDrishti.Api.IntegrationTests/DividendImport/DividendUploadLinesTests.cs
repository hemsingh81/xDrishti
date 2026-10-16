using System.Globalization;
using XDrishti.Api.Endpoints;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.DividendImport;

/// <summary>
/// The <c>includeDuplicates</c> values of the dividend import (ADR 0030): each value is capped in length and the reading stops at the first number over the limit, before the rest is parsed. No database and no host: the parser is a pure function of the form values.
/// </summary>
public sealed class DividendUploadLinesTests
{
    private static (List<int> Lines, FieldErrors Errors) Read(params string?[] values)
    {
        var errors = new FieldErrors();
        return (DividendUploads.Lines(values, errors), errors);
    }

    [Theory]
    [InlineData("[2, 5, 9]")]
    [InlineData("2,5,9")]
    [InlineData(" 2 , 5,, 9 ")]
    public void Line_numbers_are_read_from_a_json_array_or_a_comma_list(string value)
    {
        var (lines, errors) = Read(value);

        lines.ShouldBe([2, 5, 9]);
        errors.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void A_repeated_field_adds_up()
    {
        Read("2", "3,4", "[5]").Lines.ShouldBe([2, 3, 4, 5]);
    }

    [Fact]
    public void Blank_values_are_ignored()
    {
        Read(null, "", "  ").Lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2,x")]
    [InlineData("[2,\"x\"]")]
    [InlineData("99999999999")]
    [InlineData("[")]
    public void Anything_else_is_a_400_naming_the_field(string value)
    {
        var (lines, errors) = Read(value);

        lines.ShouldBeEmpty();
        errors.Items.Single().Field.ShouldBe("includeDuplicates");
    }

    [Fact]
    public void A_value_over_the_length_cap_is_refused_before_it_is_parsed()
    {
        var value = new string('1', DividendUploads.MaxLinesValueLength + 1); // not even a number: the length decides

        var (lines, errors) = Read(value);

        lines.ShouldBeEmpty();
        errors.Items.Single().Message.ShouldContain("longer than");
    }

    [Fact]
    public void A_value_at_the_length_cap_is_still_read()
    {
        var value = string.Join(',', Enumerable.Range(1, DividendCsvLimits.MaxRows).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        value.Length.ShouldBeLessThanOrEqualTo(DividendUploads.MaxLinesValueLength);

        Read(value).Lines.Count.ShouldBe(DividendCsvLimits.MaxRows);
    }

    [Fact]
    public void More_lines_than_the_limit_stop_the_reading_before_the_rest_is_parsed()
    {
        // the numbers after the limit would be a format error, but the answer is the limit: they are never parsed
        var numbers = Enumerable.Range(1, DividendCsvLimits.MaxRows + 1).Select(i => i.ToString(CultureInfo.InvariantCulture));
        var value = string.Join(',', numbers) + ",not-a-number";

        var (lines, errors) = Read(value);

        lines.ShouldBeEmpty();
        errors.Items.Single().Message.ShouldContain("At most");
    }

    [Fact]
    public void Repeated_values_over_the_limit_stop_at_the_first_that_crosses_it()
    {
        var first = string.Join(',', Enumerable.Range(1, DividendCsvLimits.MaxRows).Select(i => i.ToString(CultureInfo.InvariantCulture)));

        var (lines, errors) = Read(first, "1", "not-a-number");

        lines.ShouldBeEmpty();
        errors.Items.Single().Message.ShouldContain("At most");
    }
}
