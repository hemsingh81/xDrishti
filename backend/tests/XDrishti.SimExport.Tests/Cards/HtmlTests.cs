using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criterion 5 of P3-04c, the encoder: <c>Html.Text</c> and <c>Html.Attr</c> (C5).</summary>
public sealed class HtmlTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("\"><img src=x onerror=alert(1)>", "&quot;&gt;&lt;img src=x onerror=alert(1)&gt;")]
    [InlineData("&amp;", "&amp;amp;")]
    [InlineData("' onmouseover='x", "&#39; onmouseover=&#39;x")]
    [InlineData("</svg><script>", "&lt;/svg&gt;&lt;script&gt;")]
    [InlineData("M&M", "M&amp;M")]
    [InlineData("plain text 123", "plain text 123")]
    [InlineData("", "")]
    public void The_five_special_characters_are_encoded_in_text_and_in_attributes(string input, string expected)
    {
        Html.Text(input).ShouldBe(expected);
        Html.Attr(input).ShouldBe(expected);
    }

    [Fact]
    public void Null_is_the_empty_string()
    {
        Html.Text(null).ShouldBeEmpty();
        Html.Attr(null).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a\0b", "ab")]
    [InlineData("a\u202Eb", "ab")]
    [InlineData("a\u200Bb", "ab")]
    [InlineData("a\u2066b\u2069c", "abc")]
    [InlineData("a\uFEFFb", "ab")]
    [InlineData("a\nb\tc\rd", "a b c d")]
    public void Control_and_format_characters_and_unpaired_surrogates_are_removed(string input, string expected)
    {
        Html.Text(input).ShouldBe(expected);
    }

    [Fact]
    public void An_unpaired_surrogate_is_removed_high_or_low()
    {
        Html.Text("a" + '\ud800' + "b").ShouldBe("ab");
        Html.Text("a" + '\udc00' + "b").ShouldBe("ab");
        Html.Text("a" + '\udc00' + '\ud800' + "b").ShouldBe("ab", "a low followed by a high is two lone surrogates, not a pair");
    }

    [Fact]
    public void The_output_with_a_bidi_control_equals_the_output_without_it()
    {
        Html.Text("NSE:AB\u202EC<").ShouldBe(Html.Text("NSE:ABC<"));
    }

    [Fact]
    public void A_valid_pair_is_kept_and_a_supplementary_format_character_is_removed_and_text_is_not_turned_into_entities()
    {
        Html.Text("a\U0001F600b").ShouldBe("a\U0001F600b");
        Html.Text("a\U000E0001b").ShouldBe("ab", "U+E0001 LANGUAGE TAG is a Format character outside the BMP");
        Html.Text("rupee ₹ and −").ShouldBe("rupee ₹ and −");
    }
}
