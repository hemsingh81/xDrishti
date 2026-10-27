using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-02 AC 7: the scrubber replaces secret-shaped strings in free text, counts them, and is idempotent.</summary>
public sealed class OutputScrubberTests
{
    private const string Sha256 = "0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5";

    [Theory]
    [InlineData("pan ABCDE1234F here", "pan [redacted] here")]
    [InlineData("account 123456789 and 1234567890123", "account [redacted] and [redacted]")]
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTYifQ.c2lnbmF0dXJl end", "token [redacted] end")]
    [InlineData("key QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2 end", "key [redacted] end")]
    public void A_secret_shaped_string_is_replaced(string input, string expected)
    {
        var result = OutputScrubber.Scrub(input, out var count);

        result.ShouldBe(expected);
        count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("Reliance Industries Limited")]
    [InlineData("PAN-like but ABCDE12345 is not (digit last)")]
    [InlineData("eight digits 12345678 pass")]
    [InlineData("12,345,678,901 with commas")]
    [InlineData("========================================")]
    [InlineData("a short token abc123")]
    public void Ordinary_text_is_untouched(string input)
    {
        OutputScrubber.Scrub(input, out var count).ShouldBe(input);
        count.ShouldBe(0);
    }

    [Theory]
    [InlineData("ABCDE1234F 123456789 eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTYifQ.c2ln QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2")]
    [InlineData("nothing to hide")]
    public void Scrubbing_twice_changes_nothing_more(string input)
    {
        var once = OutputScrubber.Scrub(input, out _);

        OutputScrubber.Scrub(once, out var again).ShouldBe(once);
        again.ShouldBe(0);
    }

    [Fact]
    public void A_text_cell_with_a_hash_in_it_is_scrubbed_but_a_structured_cell_is_not()
    {
        var counters = new CleanCounters();

        CellCleaner.Clean("note " + Sha256, isText: true, counters).ShouldBe("note [redacted]");
        counters.Redactions.ShouldBe(1);

        var other = new CleanCounters();
        CellCleaner.Clean(Sha256, isText: false, other).ShouldBe(Sha256);
        CellCleaner.Clean(12_345_678_901L, isText: false, other).ShouldBe(12_345_678_901L);
        CellCleaner.Clean("12345678901", isText: false, other).ShouldBe("12345678901");
        CellCleaner.Clean(1234.5678d, isText: false, other).ShouldBe(1234.5678d);
        other.Redactions.ShouldBe(0);
    }
}
