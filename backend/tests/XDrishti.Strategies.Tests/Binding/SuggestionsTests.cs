using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>Criterion 4, "did you mean": the closest names within edit distance 2, case-insensitively, and the property that a typo of any of the 56 names suggests the right one.</summary>
public sealed class SuggestionsTests
{
    /// <summary>The words that are keywords of the language: a typo that lands on one is not a name at all.</summary>
    private static readonly string[] Reserved = ["and", "or", "not", "true", "false", "tf", "time"];

    private static readonly string[] BuiltinFunctions = ["crosses_above", "crosses_below", "rising", "falling", "between", "abs", "min", "max"];

    private static readonly string[] CallableNames = [.. IndicatorCatalog.All.Select(i => i.Name), .. BuiltinFunctions, "regime"];

    public static TheoryData<string> FeatureNames { get; } = [.. IndicatorCatalog.All.Select(i => i.Name)];

    public static TheoryData<string> BuiltinNames { get; } = [.. BuiltinFunctions];

    [Theory]
    [InlineData("rsi", "rsi", 0)]
    [InlineData("rsi", "RSI", 0)]
    [InlineData("rsi", "rs", 1)]
    [InlineData("rsi", "rsii", 1)]
    [InlineData("rsi", "rsx", 1)]
    [InlineData("rsi", "ris", 1)]
    [InlineData("rsi", "irs", 2)]
    [InlineData("ab", "ba", 1)]
    [InlineData("ca", "abc", 3)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("", "", 0)]
    [InlineData("1d", "1D", 0)]
    [InlineData("prev_day_high", "prev_day_hgih", 1)]
    [InlineData("flaw", "lawn", 2)]
    public void The_distance_is_the_optimal_string_alignment_distance_ignoring_case(string first, string second, int expected)
    {
        Suggestions.Distance(first, second).ShouldBe(expected);
        Suggestions.Distance(second, first).ShouldBe(expected, "the distance is symmetric");
        EditDistance.Between(first, second).ShouldBe(expected, "the reference implementation of the tests agrees");
    }

    [Fact]
    public void The_distance_equals_the_reference_implementation_of_the_tests_on_random_text()
    {
        var random = new Random(17);
        const string Letters = "abc_RSI1";
        string Text() => new([.. Enumerable.Range(0, random.Next(0, 9)).Select(_ => Letters[random.Next(Letters.Length)])]);

        for (var i = 0; i < 3000; i++)
        {
            var a = Text();
            var b = Text();

            Suggestions.Distance(a, b).ShouldBe(EditDistance.Between(a, b), $"'{a}' and '{b}'");
        }
    }

    [Fact]
    public void Only_the_closest_names_within_the_limit_are_returned_and_in_ordinal_order()
    {
        Suggestions.Closest("rsi", ["rsi", "rs", "rsiz", "xyz"]).ShouldBe(["rsi"]);
        Suggestions.Closest("rsii", ["rsi", "rsx", "rsiz", "rsiii", "rsia"]).ShouldBe(["rsi", "rsia", "rsiii", "rsiz"]);
        Suggestions.Closest("rsiii", ["rs", "r", "rsi"]).ShouldBe(["rsi"], "two away is still close");
        Suggestions.Closest("rsiiii", ["rs", "r", "rsi"]).ShouldBeEmpty("three away is not");
        Suggestions.Closest("abcdef", []).ShouldBeEmpty();
    }

    [Fact]
    public void At_most_five_names_are_listed()
    {
        Suggestions.Closest("abcd", ["abca", "abcb", "abcc", "abce", "abcf", "abcg", "abch"]).Count.ShouldBe(5);
    }

    [Fact]
    public void A_text_of_one_or_two_letters_is_only_matched_within_distance_one()
    {
        Suggestions.Closest("x", ["max", "min", "abs"]).ShouldBeEmpty();
        Suggestions.Closest("n", ["nr", "max"]).ShouldBe(["nr"]);
        Suggestions.Closest("ax", ["adx", "atr"]).ShouldBe(["adx"]);
        Suggestions.Closest("ab", ["abcd"]).ShouldBeEmpty();
    }

    [Fact]
    public void A_different_case_is_the_first_thing_suggested()
    {
        Suggestions.Closest("Close", ["close", "closer", "cose"]).ShouldBe(["close"]);
        Suggestions.Closest("1d", ["1D", "1m", "1M"]).ShouldBe(["1D"]);
        Suggestions.Closest("mon", ["MON", "man"]).ShouldBe(["MON"]);
    }

    [Fact]
    public void The_list_reads_as_a_sentence()
    {
        Suggestions.Listed([]).ShouldBeEmpty();
        Suggestions.Listed(["a"]).ShouldBe("`a`");
        Suggestions.Listed(["a", "b"]).ShouldBe("`a` or `b`");
        Suggestions.Listed(["a", "b", "c"]).ShouldBe("`a`, `b` or `c`");
    }

    /// <summary>The typos of one word: each letter deleted, a letter inserted at each place, two neighbours swapped, each letter replaced.</summary>
    private static IEnumerable<(string Typo, string Kind)> Typos(string word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            yield return (word.Remove(i, 1), "deleted " + word[i]);
            foreach (var letter in "xq_e")
            {
                yield return (word.Insert(i, letter.ToString()), "inserted " + letter + " before " + word[i]);
            }

            if (i + 1 < word.Length && word[i] != word[i + 1])
            {
                yield return (word[..i] + word[i + 1] + word[i] + word[(i + 2)..], "swapped " + word[i] + word[i + 1]);
            }

            yield return (word[..i] + (word[i] == 'z' ? 'y' : 'z') + word[(i + 1)..], "replaced " + word[i]);
        }

        foreach (var letter in "xq_e")
        {
            yield return (word + letter, "inserted " + letter + " at the end");
        }
    }

    private static string Written(string typo) => typo + "() > 0";

    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void A_typo_of_any_of_the_56_names_is_unknown_and_suggests_the_name_among_the_closest(string name)
    {
        var checkedTypos = 0;
        foreach (var (typo, kind) in Typos(name).Distinct())
        {
            if (typo.Length == 0 || CallableNames.Contains(typo) || Reserved.Contains(typo) || !typo.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                continue;
            }

            var tier = Tier(typo, CallableNames);
            var result = Compiling.Of(Written(typo), Timeframe.M1);

            var error = result.Errors.First();
            error.Code.ShouldBe(LintCodes.UnknownName, typo + " (" + kind + " of " + name + ")");
            error.Span.Start.ShouldBe(0);
            error.Span.End.ShouldBe(typo.Length);
            tier.ShouldContain(name, typo + " (" + kind + ") is one edit from " + name + " and must be among the closest");
            error.Suggestion.ShouldBe(tier[0], typo + " (" + kind + " of " + name + ")");
            foreach (var close in tier)
            {
                error.Message.ShouldContain("`" + close + "`", Case.Sensitive, typo + " lists " + close);
            }

            if (tier.Count == 1)
            {
                error.Suggestion.ShouldBe(name, "a typo with one neighbour suggests it");
            }

            checkedTypos++;
        }

        checkedTypos.ShouldBeGreaterThan(name.Length * 3, "the property must have been tried on many typos of " + name);
    }

    [Theory]
    [MemberData(nameof(BuiltinNames))]
    public void A_typo_of_a_builtin_function_suggests_it(string name)
    {
        foreach (var (typo, kind) in Typos(name).Distinct())
        {
            if (typo.Length == 0 || CallableNames.Contains(typo) || Reserved.Contains(typo))
            {
                continue;
            }

            var tier = Tier(typo, CallableNames);
            var error = Compiling.Of(Written(typo), Timeframe.M1).Errors.First();

            error.Code.ShouldBe(LintCodes.UnknownName, typo + " (" + kind + ")");
            tier.ShouldContain(name, typo + " (" + kind + " of " + name + ")");
            error.Suggestion.ShouldBe(tier[0]);
        }
    }

    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void A_name_in_the_wrong_case_suggests_the_name_itself(string name)
    {
        var upper = name.ToUpperInvariant();

        var error = Compiling.Of(upper + "() > 0", Timeframe.M1).Errors.First();

        error.Code.ShouldBe(LintCodes.UnknownName);
        error.Suggestion.ShouldBe(name);
    }

    [Fact]
    public void A_typo_of_a_name_as_a_value_suggests_names_of_values_too()
    {
        Compiling.OnlyError("todya_open > 1").Suggestion.ShouldBe("today_open");
        Compiling.OnlyError("vixx > 1").Suggestion.ShouldBe("vix");
        Compiling.OnlyError("day_of_wek == 1").Suggestion.ShouldBe("day_of_week");
        Compiling.OnlyError("day_of_week == TUES").Suggestion.ShouldBe("TUE");
        Compiling.OnlyError("rsi(rsi_ln) > 1", parameters: Compiling.Params(("rsi_len", 14))).Suggestion.ShouldBe("rsi_len");
        Compiling.OnlyError("Truee").Suggestion.ShouldBe("true");
        Compiling.OnlyError("True").Suggestion.ShouldBe("true");
    }

    /// <summary>The names at the smallest distance from the typo, found with the reference implementation, in ordinal order, at most five.</summary>
    private static List<string> Tier(string typo, string[] vocabulary)
    {
        var limit = Math.Min(2, Math.Max(1, typo.Length - 1));
        var distances = vocabulary.Distinct().Select(n => (Name: n, Distance: EditDistance.Between(typo, n))).Where(p => p.Distance <= limit).ToList();
        if (distances.Count == 0)
        {
            return [];
        }

        var best = distances.Min(p => p.Distance);
        return [.. distances.Where(p => p.Distance == best).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).Take(5)];
    }
}
