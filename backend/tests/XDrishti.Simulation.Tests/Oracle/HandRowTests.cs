
namespace XDrishti.Simulation.Tests.Oracle;

public sealed class HandRowTests
{
    [Theory]
    [MemberData(nameof(AllRows.Ids), MemberType = typeof(AllRows))]
    public void Row_equals_the_copied_numbers_in_the_modelled_mode(string id)
    {
        List<string> bad = RowCheck.Verify(AllRows.Get(id), exactMode: false);
        bad.ShouldBeEmpty(string.Join("; ", bad));
    }

    [Theory]
    [MemberData(nameof(AllRows.Ids), MemberType = typeof(AllRows))]
    public void Row_equals_the_copied_numbers_with_no_slippage(string id)
    {
        List<string> bad = RowCheck.Verify(AllRows.Get(id), exactMode: true);
        bad.ShouldBeEmpty(string.Join("; ", bad));
    }
}
