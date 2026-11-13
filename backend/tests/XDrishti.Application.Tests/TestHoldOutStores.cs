#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Tests;

/// <summary>A substituted hold-out store that answers the chain reads with an empty chain (nothing released), so a handler that reads the released blocks gets a list and not a null.</summary>
internal static class TestHoldOutStores
{
    public static IHoldOutStore NoReleases()
    {
        var store = Substitute.For<IHoldOutStore>();
        store.ListBlocksAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredBlock>>([]));
        return store;
    }
}
