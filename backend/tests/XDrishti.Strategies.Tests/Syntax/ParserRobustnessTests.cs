using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Syntax;

/// <summary>
/// Nothing the parser is given makes it throw or hang, and what it returns always obeys the contract: a tree and no problems, or no tree and problems in source order that lie inside the text, with the line and
/// column of their start and the code of a syntax problem. The inputs are random mixes of the pieces of the language and of things that are not, and single-character damage to valid conditions.
/// </summary>
public sealed class ParserRobustnessTests
{
    private static readonly string[] Pieces =
    [
        "a", "close", "rsi", "tf", "tf(\"1h\")", "prev_day", "(", ")", "[", "]", ",", ".", "=", "==", "!=", "<", "<=", ">", ">=", "+", "-", "*", "/", "and", "or", "not", "true", "false",
        "1", "0", "14", "0.5", "1e-3", "1e400", "55abc", "\"x\"", "\"", "\"09:45\"", "$", "＞", " ", "😀", "'q'", "!", "&&", "||", "\n", "\t", "  ", "source", "#", "é", "\uD800",
    ];

    private static string RandomSource(Random random)
    {
        var count = random.Next(0, 24);
        var parts = Enumerable.Range(0, count).Select(_ => Pieces[random.Next(Pieces.Length)]);
        return string.Join(random.Next(3) == 0 ? string.Empty : " ", parts);
    }

    private static void ShouldObeyTheContract(string source, ParseResult result)
    {
        if (result.IsSuccess)
        {
            result.Problems.ShouldBeEmpty(source);
            var tree = result.Tree.ShouldNotBeNull(source);
            ConditionParser.Parse(ExpressionPrinter.Print(tree)).Tree.ShouldBe(tree, source);
            return;
        }

        result.Tree.ShouldBeNull(source);
        result.Problems.ShouldNotBeEmpty(source);
        result.Problems.Select(p => p.Span.Start).ShouldBe([.. result.Problems.Select(p => p.Span.Start).Order()], source);
        foreach (var problem in result.Problems)
        {
            problem.Code.ShouldStartWith("strategy.syntax.", Case.Sensitive, source);
            problem.Message.ShouldNotBeNullOrWhiteSpace(source);
            problem.Severity.ShouldBe(ProblemSeverity.Error, source);
            problem.Span.Start.ShouldBeInRange(0, source.Length, source);
            problem.Span.End.ShouldBeInRange(problem.Span.Start, source.Length, source);
            (problem.Span.Line, problem.Span.Column).ShouldBe(SourceLocator.At(source, problem.Span.Start), source);
        }
    }

    [Fact]
    public void Random_mixes_of_language_and_junk_never_throw_and_always_obey_the_contract()
    {
        var random = new Random(2026);
        var successes = 0;
        for (var i = 0; i < 6000; i++)
        {
            var source = RandomSource(random);

            var result = ConditionParser.Parse(source);

            ShouldObeyTheContract(source, result);
            successes += result.IsSuccess ? 1 : 0;
        }

        // The mix must produce valid conditions as well as invalid ones, or the test would only look at one side.
        successes.ShouldBeGreaterThan(50);
        successes.ShouldBeLessThan(5000);
    }

    [Fact]
    public void Damaging_a_valid_condition_by_one_character_never_throws_and_always_obeys_the_contract()
    {
        var random = new Random(99);
        string[] damage = ["(", ")", "[", "]", ",", ".", "=", "<", "-", "\"", "$", "＞", "1", "a", " ", "\n", "e", "😀"];
        var sources = ValidSources.Cases.Keys.ToArray();
        var rejected = 0;
        for (var i = 0; i < 4000; i++)
        {
            var source = sources[random.Next(sources.Length)];
            var at = random.Next(0, source.Length + 1);
            var damaged = random.Next(3) switch
            {
                0 when source.Length > 0 && at < source.Length => source.Remove(at, 1),
                1 => source.Insert(at, damage[random.Next(damage.Length)]),
                _ when source.Length > 0 && at < source.Length => source[..at] + damage[random.Next(damage.Length)] + source[(at + 1)..],
                _ => source + damage[random.Next(damage.Length)],
            };

            var result = ConditionParser.Parse(damaged);

            ShouldObeyTheContract(damaged, result);
            rejected += result.IsSuccess ? 0 : 1;
        }

        rejected.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public void Every_prefix_of_every_valid_condition_is_read_or_refused_with_problems()
    {
        foreach (var source in ValidSources.Cases.Keys)
        {
            for (var length = 0; length <= source.Length; length++)
            {
                var prefix = source[..length];

                ShouldObeyTheContract(prefix, ConditionParser.Parse(prefix));
            }
        }
    }

    [Fact]
    public void A_text_made_of_nothing_but_faults_gets_problems_that_obey_the_contract()
    {
        foreach (var unit in new[] { "$ ", "a b ", "( ", ") ", "f(, ", "x[ ", "1e400 ", "\" ", "tf( ", "a < b < ", ", ", ". ", "= ", "and ", "not ", "- " })
        {
            var source = string.Concat(Enumerable.Repeat(unit, 4000 / unit.Length));

            ShouldObeyTheContract(source, ConditionParser.Parse(source));
        }
    }

    [Fact]
    public void Parsing_is_deterministic_and_does_not_depend_on_what_was_parsed_before()
    {
        string[] sources = ["rsi(14) > 55", "a < b < c", "$", "tf(\"1h\")", "(", string.Empty, "rsi(14) > 55"];

        var first = sources.Select(s => ConditionParser.Parse(s)).ToArray();
        var second = sources.Reverse().Select(s => ConditionParser.Parse(s)).Reverse().ToArray();

        for (var i = 0; i < sources.Length; i++)
        {
            second[i].Tree.ShouldBe(first[i].Tree);
            second[i].Problems.ShouldBe(first[i].Problems);
        }
    }

    [Fact]
    public void Parsing_from_many_threads_at_once_gives_the_same_results()
    {
        string[] sources = ["rsi(14) > 55 and close > vwap", "a < b < c", "$ > 1", "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)", "((a)", "x[1.5]"];
        var expected = sources.Select(s => ConditionParser.Parse(s)).ToArray();

        var results = Enumerable.Range(0, 64).AsParallel().WithDegreeOfParallelism(8).Select(i => (i, ConditionParser.Parse(sources[i % sources.Length]))).ToArray();

        foreach (var (i, result) in results)
        {
            result.Tree.ShouldBe(expected[i % sources.Length].Tree);
            result.Problems.ShouldBe(expected[i % sources.Length].Problems);
        }
    }
}
