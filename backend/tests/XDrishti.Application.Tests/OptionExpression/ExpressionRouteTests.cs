using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>P14-09 criterion 8: the shared route lists the expression family beside the P15 and pairs families, and still refuses everything else.</summary>
public sealed class ExpressionRouteTests
{
    private static ExtensionEvaluationRecord Record() =>
        new(new string('a', 64), "expr:option_buy", "O", null, "expr-1", "none", new string('b', 64), null, new string('c', 64), 3, 1.4, "quarter", 3, 0, 0, null, "{}", "NOT_EVALUABLE", ["folds 0 of 6"], ["future baseline: NOT_EVALUABLE no_futures_bars"], "{}", "{}", false);

    [Fact]
    public async Task The_list_accepts_the_expression_family_and_returns_its_records()
    {
        var store = Substitute.For<IExtensionEvaluationStore>();
        store.ListAsync("expr:option_buy", 100, Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<ExtensionEvaluationRecord>)[Record()]);

        var result = await new ListExtensionEvaluationsHandler(store).HandleAsync(new ListExtensionEvaluationsQuery("expr:option_buy"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Family.ShouldBe("expr:option_buy");
        var view = result.Value.Items.ShouldHaveSingleItem();
        (view.Protocol, view.Verdict, view.FoldUnit, view.VersionKey).ShouldBe(("O", "NOT_EVALUABLE", "quarter", "expr-1"));
    }

    [Theory]
    [InlineData("expr:option_sell")]
    [InlineData("expr")]
    [InlineData(null)]
    public async Task Any_other_family_is_still_refused_and_the_message_names_the_expression_family(string? family)
    {
        var result = await new ListExtensionEvaluationsHandler(Substitute.For<IExtensionEvaluationStore>()).HandleAsync(new ListExtensionEvaluationsQuery(family), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
        result.Error.Message.ShouldContain("expr:option_buy");
        result.Error.Message.ShouldContain("pairs:intraday-1");
    }
}
