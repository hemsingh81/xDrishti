using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.Pairs;

/// <summary>P14-13: the shared route lists the pairs family beside the P15 families, and still refuses everything else.</summary>
public sealed class PairsRouteTests
{
    private static ExtensionEvaluationRecord Record() =>
        new(new string('a', 64), "pairs:intraday-1", "S", null, "pairs-1", "none", new string('b', 64), null, new string('c', 64), 2006, 3.74, "quarter", 3, 0, 0, null, "{}", "NOT_EVALUABLE", ["quarters 0 of 6"], ["ban_list: unavailable"], "{}", "{}", false);

    [Fact]
    public async Task The_list_accepts_the_pairs_family_and_returns_its_records()
    {
        var store = Substitute.For<IExtensionEvaluationStore>();
        store.ListAsync("pairs:intraday-1", 100, Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<ExtensionEvaluationRecord>)[Record()]);

        var result = await new ListExtensionEvaluationsHandler(store).HandleAsync(new ListExtensionEvaluationsQuery("pairs:intraday-1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Family.ShouldBe("pairs:intraday-1");
        var view = result.Value.Items.ShouldHaveSingleItem();
        (view.Protocol, view.Verdict, view.FoldUnit, view.Trials).ShouldBe(("S", "NOT_EVALUABLE", "quarter", 2006));
    }

    [Theory]
    [InlineData("pairs:intraday-2")]
    [InlineData("pairs")]
    [InlineData(null)]
    public async Task Any_other_family_is_still_refused_and_the_message_names_the_pairs_family(string? family)
    {
        var result = await new ListExtensionEvaluationsHandler(Substitute.For<IExtensionEvaluationStore>()).HandleAsync(new ListExtensionEvaluationsQuery(family), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
        result.Error.Message.ShouldContain("pairs:intraday-1");
        result.Error.Message.ShouldContain("pead");
    }
}
