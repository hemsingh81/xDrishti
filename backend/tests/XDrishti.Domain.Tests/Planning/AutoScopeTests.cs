using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>AU-16 criterion 1: the scope takes baskets and stocks, needs at least one, and has a stable hash.</summary>
public sealed class AutoScopeTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void A_scope_with_nothing_in_it_is_refused()
    {
        AutoScope.Create(null, null).Error.Code.ShouldBe(AutoScope.EmptyCode);
        AutoScope.Create([], [Guid.Empty]).Error.Code.ShouldBe(AutoScope.EmptyCode);
    }

    [Fact]
    public void Baskets_alone_or_stocks_alone_are_accepted()
    {
        AutoScope.Create([A], null).IsSuccess.ShouldBeTrue();
        AutoScope.Create(null, [B]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_same_choice_in_any_order_with_duplicates_has_the_same_hash_and_another_choice_another()
    {
        var one = AutoScope.Create([A, B, A], [B]).Value;
        var two = AutoScope.Create([B, A], [B, B]).Value;
        var other = AutoScope.Create([A], [B]).Value;

        one.Hash.ShouldBe(two.Hash);
        one.BasketIds.ShouldBe([A, B]);
        one.Hash.ShouldNotBe(other.Hash);
        one.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void More_than_the_most_entries_is_refused()
    {
        var many = Enumerable.Range(1, AutoScope.MaxEntries + 1).Select(_ => Guid.NewGuid()).ToList();

        AutoScope.Create(many, null).Error.Code.ShouldBe(AutoScope.TooLargeCode);
    }
}
