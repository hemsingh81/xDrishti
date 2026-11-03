using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.Learning;

/// <summary>P14-11 criterion 6: the shared route lists <c>ext:iv-1</c> without the P15 list (and so the P15 phase check) knowing it, and the use case routes it to the comparison.</summary>
public sealed class IvRouteTests
{
    private static ExtensionEvaluationRecord Record() =>
        new(new string('a', 64), "ext:iv-1", "X", "iv-1", "iv-1:iv_atm", "tf-1", new string('b', 64), null, new string('c', 64), 1, 1.0, "quarter", 3, 0, 0, null, "{}", "NOT_EVALUABLE", ["folds 0 of 6"], [], "{}", "{}", false);

    [Fact]
    public async Task The_list_accepts_the_iv_family_and_returns_its_records()
    {
        var store = Substitute.For<IExtensionEvaluationStore>();
        store.ListAsync("ext:iv-1", 100, Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyList<ExtensionEvaluationRecord>)[Record()]);

        var result = await new ListExtensionEvaluationsHandler(store).HandleAsync(new ListExtensionEvaluationsQuery("ext:iv-1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var view = result.Value.Items.ShouldHaveSingleItem();
        (view.Family, view.Protocol, view.ExtensionVersion).ShouldBe(("ext:iv-1", "X", "iv-1"));
    }

    [Fact]
    public async Task Another_extension_family_is_still_refused_and_the_message_names_the_iv_family()
    {
        var result = await new ListExtensionEvaluationsHandler(Substitute.For<IExtensionEvaluationStore>()).HandleAsync(new ListExtensionEvaluationsQuery("ext:iv-2"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
        result.Error.Message.ShouldContain("ext:iv-1");
    }

    [Fact]
    public void The_p15_list_the_phase_check_iterates_does_not_contain_the_iv_family()
    {
        ExtensionFamilies.All.ShouldNotContain("ext:iv-1");
        ExtensionFamilies.All.Count.ShouldBe(5);
    }
}
