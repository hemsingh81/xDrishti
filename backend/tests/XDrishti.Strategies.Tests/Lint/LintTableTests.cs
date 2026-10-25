using System.Reflection;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>
/// Criterion 6: every row of the lint table has failing examples (exact code, span, line and column, suggestion and message text) and near-misses that pass. The table is in <see cref="LintCases"/> and the messages in
/// <see cref="LintMessages"/>; the rows themselves are written out here from the spec.
/// </summary>
public sealed class LintTableTests
{
    /// <summary>The rows of the spec's table, in its order, and the two codes that were added to it (a parameter that hides a name; a lagged read across a session open).</summary>
    private static readonly string[] Rows =
    [
        "unknown_name", "bad_arguments", "unsupported_timeframe", "negative_or_zero_lag", "lag_too_deep", "lag_on_non_feature", "mixed_timeframes_in_lag", "time_on_daily", "bad_time_literal",
        "type_mismatch", "constant_condition", "redundant_timeframe", "needs_context", "needs_expiry_days", "shadowed_name", "lag_across_session", "never_true",
    ];

    private static string Label(string code) => code + ": ";

    [Fact]
    public void The_codes_are_the_rows_of_the_table_with_the_stable_prefix()
    {
        var codes = typeof(LintCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        codes.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(Rows.Select(r => "strategy.lint." + r).OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_row_has_at_least_one_failing_example_and_one_near_miss()
    {
        foreach (var row in Rows)
        {
            LintCases.Failing.Keys.Count(k => k.StartsWith(Label(row), StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(1, row + " has no failing example");
            LintCases.Passing.Keys.Count(k => k.StartsWith(Label(row), StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(1, row + " has no near-miss");
        }
    }

    [Fact]
    public void Every_label_starts_with_a_row_and_a_failing_example_produces_a_problem_of_that_row()
    {
        foreach (var (label, lintCase) in LintCases.Failing)
        {
            var row = Rows.SingleOrDefault(r => label.StartsWith(Label(r), StringComparison.Ordinal));
            row.ShouldNotBeNull(label);
            lintCase.Problems.ShouldContain(p => p.Code == "strategy.lint." + row, label);
        }

        LintCases.Passing.Keys.ShouldAllBe(k => Rows.Any(r => k.StartsWith(Label(r), StringComparison.Ordinal)));
    }

    [Fact]
    public void The_message_of_every_problem_of_every_failing_example_is_pinned()
    {
        LintMessages.Cases.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(LintCases.Failing.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (label, lintCase) in LintCases.Failing)
        {
            LintMessages.Cases[label].Length.ShouldBe(lintCase.Problems.Length, label);
        }
    }

    [Theory]
    [MemberData(nameof(LintCases.FailingLabels), MemberType = typeof(LintCases))]
    public void A_failing_example_produces_exactly_the_expected_problems_in_source_order(string label)
    {
        var lintCase = LintCases.Failing[label];
        var expected = lintCase.Problems.Select((p, i) => p with { Message = LintMessages.Cases[label][i] }).ToArray();

        var result = ConditionCompiler.Compile(lintCase.Source, new CompileContext(lintCase.Evaluation, lintCase.Parameters));

        ProblemAssert.Match(lintCase.Source, result.Problems, expected);
        result.IsSuccess.ShouldBe(expected.All(p => p.Severity != ProblemSeverity.Error), label);
        result.Problems.Select(p => p.Span.Start).ShouldBeInOrder();
    }

    /// <summary>
    /// The convention behind every suggestion of the table: it is the text that replaces the span of its own problem. A tool (the editor's quick fix, the assistant) can apply it mechanically, and the condition then still
    /// reads (a suggestion that breaks the syntax would only trade the problem for another) and has one problem of that code less. Another problem may show up (<c>closee[0]</c> becomes <c>close[0]</c>), which is the
    /// next round of fixes.
    /// </summary>
    [Theory]
    [MemberData(nameof(LintCases.SuggestingLabels), MemberType = typeof(LintCases))]
    public void Replacing_the_span_of_a_problem_with_its_suggestion_removes_that_problem(string label)
    {
        var lintCase = LintCases.Failing[label];
        var context = new CompileContext(lintCase.Evaluation, lintCase.Parameters);
        var before = ConditionCompiler.Compile(lintCase.Source, context).Problems;

        foreach (var problem in before.Where(p => p.Suggestion is not null))
        {
            var fixedSource = string.Concat(lintCase.Source.AsSpan(0, problem.Span.Start), problem.Suggestion, lintCase.Source.AsSpan(problem.Span.End));
            var after = ConditionCompiler.Compile(fixedSource, context);

            var where = label + ": " + lintCase.Source + " -> " + fixedSource + " -> " + Compiling.Describe(after);
            after.Problems.ShouldNotContain(p => p.Code.StartsWith("strategy.syntax.", StringComparison.Ordinal), where);
            after.Problems.Count(p => p.Code == problem.Code).ShouldBe(before.Count(p => p.Code == problem.Code) - 1, where);
        }
    }

    [Theory]
    [MemberData(nameof(LintCases.PassingLabels), MemberType = typeof(LintCases))]
    public void A_near_miss_compiles_and_does_not_produce_the_problem_of_its_row(string label)
    {
        var nearMiss = LintCases.Passing[label];

        var result = ConditionCompiler.Compile(nearMiss.Source, new CompileContext(nearMiss.Evaluation, nearMiss.Parameters));

        result.IsSuccess.ShouldBeTrue(nearMiss.Source + " -> " + Compiling.Describe(result));
        result.Problems.ShouldNotContain(p => p.Code == nearMiss.Code, nearMiss.Source);
    }

    [Fact]
    public void Only_a_failing_compile_has_no_plan_and_a_good_one_has_the_warnings_of_its_problems()
    {
        var failed = Compiling.Of("closee > 1");
        var warned = Compiling.Of("1 < 2");

        failed.Condition.ShouldBeNull();
        failed.IsSuccess.ShouldBeFalse();
        failed.Errors.ShouldHaveSingleItem();
        warned.Condition.ShouldNotBeNull();
        warned.Errors.ShouldBeEmpty();
        warned.Condition!.Warnings.ShouldBe(warned.Problems.Where(p => p.Severity == ProblemSeverity.Warning));
        warned.Condition.Warnings.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.ConstantCondition);
    }
}
