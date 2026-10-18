using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Tests;

/// <summary>The allocation-free number format of the export is the same text as <see cref="CanonicalText.Number(double)"/> (E2), and refuses what it must not write.</summary>
public sealed class NumberTextTests
{
    private static string Write(double value)
    {
        Span<char> text = stackalloc char[NumberText.MaxDoubleChars];
        return new string(text[..NumberText.Write(value, text)]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.5)]
    [InlineData(0.1)]
    [InlineData(0.30000000000000004)]
    [InlineData(1e-7)]
    [InlineData(1e21)]
    [InlineData(123456789.123456789)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(100.55)]
    [InlineData(-0.050000000000000044)]
    public void It_is_the_text_of_the_canonical_number(double value)
    {
        Write(value).ShouldBe(CanonicalText.Number(value));
    }

    [Fact]
    public void It_is_the_text_of_the_canonical_number_for_many_doubles_and_parses_back_to_the_same_value()
    {
        var random = new Random(20261018);
        var bits = new byte[8];
        for (var i = 0; i < 20_000; i++)
        {
            random.NextBytes(bits);
            var value = BitConverter.ToDouble(bits);
            if (!double.IsFinite(value))
            {
                continue;
            }

            var text = Write(value);
            text.ShouldBe(CanonicalText.Number(value));
            double.Parse(text, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(value == 0 ? 0 : value);
        }
    }

    [Fact]
    public void Negative_zero_is_zero()
    {
        Write(-0.0).ShouldBe("0");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_a_number_is_refused(double value)
    {
        Should.Throw<ArgumentException>(() => Write(value));
        CanonicalText.Number(double.NaN).ShouldBe("NaN", "the canonical text would write it: this is why the export has its own check");
    }

    [Fact]
    public void A_destination_that_is_too_short_is_refused()
    {
        Should.Throw<ArgumentException>(() => NumberText.Write(1.0 / 3, new char[4]));
    }
}
