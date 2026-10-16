using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;
using static XDrishti.Strategies.Tests.Support.Compiling;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>
/// Criterion 4, arguments: the number, kind and range of the arguments of every feature with parameters are checked against the catalog, and the message quotes the catalog's names, ranges and defaults. The catalog's
/// own parameters drive the cases, so a feature added to it is covered.
/// </summary>
public sealed class ArgumentTests
{
    public static TheoryData<string> NamesWithParameters { get; } = [.. IndicatorCatalog.All.Where(i => i.Parameters.Count > 0).Select(i => i.Name)];

    public static TheoryData<string> NamesWithoutParameters { get; } = [.. IndicatorCatalog.All.Where(i => i.Parameters.Count == 0).Select(i => i.Name)];

    private static IndicatorInfo Info(string name)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
        return info;
    }

    /// <summary>A call of the feature with the default for every parameter except the one at <paramref name="index"/>, which gets <paramref name="value"/>; the span of that argument.</summary>
    private static (string Source, int Start, int End) CallWith(IndicatorInfo info, int index, double value)
    {
        var text = $"{info.Name}(";
        var start = 0;
        var end = 0;
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            if (i > 0)
            {
                text += ", ";
            }

            var written = (i == index ? value : info.Parameters[i].Default).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (i == index)
            {
                start = text.Length;
                end = start + written.Length;
            }

            text += written;
        }

        return (text + ") > 0", start, end);
    }

    private static string Range(IndicatorParameter p) => $"from {p.Min.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} to {p.Max.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void A_value_above_the_range_is_refused_at_the_argument_and_the_message_quotes_the_catalogs_names_ranges_and_defaults(string name)
    {
        var info = Info(name);
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            var parameter = info.Parameters[i];
            var (source, start, end) = CallWith(info, i, parameter.Max + 1);

            var error = OnlyError(source, Timeframe.M1);

            error.Code.ShouldBe(LintCodes.BadArguments, source);
            (error.Span.Start, error.Span.End).ShouldBe((start, end), source);
            foreach (var p in info.Parameters)
            {
                error.Message.ShouldContain("`" + p.Name + "`", Case.Sensitive, source + " names " + p.Name);
                error.Message.ShouldContain(Range(p), Case.Sensitive, source + " quotes the range of " + p.Name);
                error.Message.ShouldContain("default " + p.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive, source + " quotes the default of " + p.Name);
            }

            error.Message.ShouldContain("'" + parameter.Name + "' must be between", Case.Sensitive, source + " says which parameter is wrong");
        }
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void A_value_below_the_range_is_refused_at_the_argument(string name)
    {
        var info = Info(name);
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            var (source, start, end) = CallWith(info, i, info.Parameters[i].Min - 1);

            var error = OnlyError(source, Timeframe.M1);

            error.Code.ShouldBe(LintCodes.BadArguments, source);
            (error.Span.Start, error.Span.End).ShouldBe((start, end), source);
        }
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void The_ends_of_the_range_are_accepted_when_the_catalog_accepts_the_feature_with_them(string name)
    {
        var info = Info(name);
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            foreach (var value in new[] { info.Parameters[i].Min, info.Parameters[i].Max })
            {
                var spec = info.DefaultSpec(Timeframe.M1);
                spec = i switch { 0 => spec with { P1 = value }, 1 => spec with { P2 = value }, 2 => spec with { P3 = value }, _ => spec with { P4 = value } };
                var (source, _, _) = CallWith(info, i, value);

                var catalogAccepts = FeatureFrame.Create([spec]).IsSuccess;
                var result = Of(source, Timeframe.M1);

                result.IsSuccess.ShouldBe(catalogAccepts, source + " -> " + Describe(result));
                if (catalogAccepts)
                {
                    result.Condition!.Features.ShouldBe([spec]);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void A_fraction_is_refused_for_a_whole_number_parameter_and_accepted_for_a_number_parameter(string name)
    {
        var info = Info(name);
        for (var i = 0; i < info.Parameters.Count; i++)
        {
            var parameter = info.Parameters[i];
            var value = Math.Min(parameter.Max - 0.5, Math.Max(parameter.Min + 0.5, parameter.Default + 0.5));
            var (source, start, end) = CallWith(info, i, value);

            var result = Of(source, Timeframe.M1);

            if (parameter.Kind == ParameterKind.Whole)
            {
                var error = result.Errors.ShouldHaveSingleItem(source);
                error.Code.ShouldBe(LintCodes.BadArguments);
                (error.Span.Start, error.Span.End).ShouldBe((start, end));
                error.Message.ShouldContain("whole number", Case.Sensitive);
            }
            else
            {
                result.Errors.ShouldBeEmpty(source);
            }
        }
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void One_argument_too_many_is_refused_at_the_extra_argument_and_the_message_says_how_many_are_taken(string name)
    {
        var info = Info(name);
        var arguments = string.Join(", ", info.Parameters.Select(p => p.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        var source = $"{info.Name}({arguments}, 1) > 0";

        var error = OnlyError(source, Timeframe.M1);

        error.Code.ShouldBe(LintCodes.BadArguments);
        error.Span.Start.ShouldBe(source.IndexOf(", 1)", StringComparison.Ordinal) + 2);
        error.Span.End.ShouldBe(error.Span.Start + 1);
        error.Message.ShouldContain($"at most {info.Parameters.Count} argument", Case.Sensitive);
        error.Message.ShouldContain($"but {info.Parameters.Count + 1} were given", Case.Sensitive, "how many were given");
    }

    [Theory]
    [MemberData(nameof(NamesWithoutParameters))]
    public void A_feature_without_parameters_takes_no_arguments(string name)
    {
        var info = Info(name);
        if (name == "regime")
        {
            return;
        }

        var error = OnlyError($"{name}(1) > 0", Timeframe.M1);

        error.Code.ShouldBe(LintCodes.BadArguments);
        error.Message.ShouldContain("takes no arguments", Case.Sensitive);
        info.Parameters.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void A_feature_with_parameters_written_without_parentheses_is_refused_with_the_call_as_the_suggestion(string name)
    {
        var error = OnlyError(name + " > 0", Timeframe.M1);

        error.Code.ShouldBe(LintCodes.BadArguments);
        error.Suggestion.ShouldBe(name + "()");
        error.Span.Start.ShouldBe(0);
        error.Span.End.ShouldBe(name.Length);
    }

    [Theory]
    [MemberData(nameof(NamesWithParameters))]
    public void The_source_is_accepted_exactly_for_the_features_that_read_a_price(string name)
    {
        var info = Info(name);
        foreach (var source in new[] { "close", "open", "high", "low" })
        {
            var result = Of($"{name}(source={source}) > 0", Timeframe.M1);

            if (info.UsesSource)
            {
                result.IsSuccess.ShouldBeTrue(name + " " + source + " -> " + Describe(result));
                var spec = result.Condition!.Features.ShouldHaveSingleItem();
                spec.Source.ShouldBe(source switch { "open" => PriceSource.Open, "high" => PriceSource.High, "low" => PriceSource.Low, _ => PriceSource.Close });
            }
            else
            {
                var error = result.Errors.ShouldHaveSingleItem(name);
                error.Code.ShouldBe(LintCodes.BadArguments);
                error.Message.ShouldContain("reads the whole bar", Case.Sensitive);
            }
        }
    }

    [Fact]
    public void Named_arguments_may_come_before_or_after_the_positional_ones()
    {
        Condition("ema(source=high, 30) > 0").Features.ShouldBe(Condition("ema(30, source=high) > 0").Features);
        Condition("bb_upper(source=low, 10, 3) > 0").Features.ShouldBe([Specs.Of("bb_upper", Timeframe.M15, 10, 3, source: PriceSource.Low)]);
    }

    [Fact]
    public void Several_wrong_arguments_of_one_call_are_all_reported_in_order()
    {
        var problems = Failures("bb_upper(0, 101, 5) > 0");

        problems.Select(p => p.Code).ShouldBe([LintCodes.BadArguments, LintCodes.BadArguments, LintCodes.BadArguments]);
        problems.Select(p => p.Span.Start).ShouldBe(["bb_upper(".Length, "bb_upper(0, ".Length, "bb_upper(0, 101, ".Length]);
        problems.Select(p => p.Span.Length).ShouldBe([1, 3, 1]);
    }

    [Fact]
    public void The_arguments_of_a_function_are_checked_by_count_and_type_with_the_signature_in_the_message()
    {
        var few = OnlyError("crosses_above(close)");
        var many = OnlyError("between(rsi(14), 30, 70, 90)");
        var kind = OnlyError("crosses_above(close, close > 1)");

        few.Message.ShouldContain("crosses_above(a, b)", Case.Sensitive);
        many.Message.ShouldContain("between(x, low, high)", Case.Sensitive);
        kind.Code.ShouldBe(LintCodes.TypeMismatch);
        kind.Message.ShouldContain("Argument 2", Case.Sensitive);
    }
}
