using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>BK-11b: <c>--members dated|single_snapshot</c> goes with <c>--basket</c> only; none is what it was.</summary>
public sealed class SimulateMembersOptionsTests
{
    private const string Window = "--strategy ORB --from 2026-03-02 --to 2026-03-04 --gross";

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), ["ORB"]);

    [Theory]
    [InlineData("dated")]
    [InlineData("single_snapshot")]
    public void A_basis_is_taken_with_a_basket(string basis)
    {
        Parse("--basket Nifty --members " + basis + " " + Window).Options!.Members.ShouldBe(basis);
    }

    [Fact]
    public void Without_the_flag_there_is_no_basis()
    {
        Parse("--basket Nifty " + Window).Options!.Members.ShouldBeNull();
    }

    [Fact]
    public void A_basis_with_symbols_or_an_unknown_basis_is_a_usage_error_naming_the_flag()
    {
        Parse("--symbols RELIANCE --members dated " + Window).Errors.ShouldContain(e => e.Contains("'--members' applies to --basket", StringComparison.Ordinal));
        Parse("--basket Nifty --members weekly " + Window).Errors.ShouldContain(e => e.Contains("'--members' is dated or single_snapshot", StringComparison.Ordinal));
    }
}
