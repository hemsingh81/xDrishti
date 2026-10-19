using XDrishti.Application.Common;

namespace XDrishti.Application.Tests.Common;

public sealed class CollectionChecksTests
{
    private static readonly int[] Empty = [];
    private static readonly int[] Distinct = [1, 2, 3];

    [Fact]
    public void HasDuplicates_is_false_for_empty_and_distinct()
    {
        Assert.False(Empty.HasDuplicates());
        Assert.False(Distinct.HasDuplicates());
    }

    [Fact]
    public void HasDuplicates_is_true_when_a_value_repeats()
    {
        var id = Guid.NewGuid();
        Assert.True(new[] { id, Guid.NewGuid(), id }.HasDuplicates());
    }
}
