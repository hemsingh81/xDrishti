using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

public sealed class KleeneTests
{
    private const SetupTruth T = SetupTruth.True;
    private const SetupTruth F = SetupTruth.False;
    private const SetupTruth U = SetupTruth.Unknown;

    [Theory]
    [InlineData(T, T, T)]
    [InlineData(T, F, F)]
    [InlineData(T, U, U)]
    [InlineData(F, U, F)]
    [InlineData(U, U, U)]
    [InlineData(F, F, F)]
    public void And_follows_the_kleene_table(SetupTruth a, SetupTruth b, SetupTruth expected)
    {
        Assert.Equal(expected, Kleene.And(a, b));
        Assert.Equal(expected, Kleene.And(b, a));
    }

    [Theory]
    [InlineData(T, T, T)]
    [InlineData(T, F, T)]
    [InlineData(T, U, T)]
    [InlineData(F, U, U)]
    [InlineData(U, U, U)]
    [InlineData(F, F, F)]
    public void Or_follows_the_kleene_table(SetupTruth a, SetupTruth b, SetupTruth expected)
    {
        Assert.Equal(expected, Kleene.Or(a, b));
        Assert.Equal(expected, Kleene.Or(b, a));
    }

    [Fact]
    public void Not_keeps_unknown_unknown()
    {
        Assert.Equal(F, Kleene.Not(T));
        Assert.Equal(T, Kleene.Not(F));
        Assert.Equal(U, Kleene.Not(U));
    }

    [Fact]
    public void Fold_of_nothing_is_true_for_all_and_false_for_any_and_unknown_is_never_true()
    {
        Assert.Equal(T, Kleene.Fold([], all: true));
        Assert.Equal(F, Kleene.Fold([], all: false));
        Assert.Equal(U, Kleene.Fold([T, U], all: true));
        Assert.Equal(U, Kleene.Fold([F, U], all: false));
        Assert.Equal(F, Kleene.Fold([T, U, F], all: true));
        Assert.Equal(T, Kleene.Fold([F, U, T], all: false));
    }
}
