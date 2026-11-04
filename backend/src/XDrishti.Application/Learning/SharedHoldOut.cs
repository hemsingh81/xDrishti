using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Learning;

/// <summary>The start of the Lab's locked hold-out, which the ML harness shares so a model and a strategy keep the same block (spec CF-06, D4).</summary>
internal static class SharedHoldOut
{
    /// <summary>The stored block's start, or null (no store, or no block fixed yet): the harness then keeps its own rule, unchanged.</summary>
    public static async ValueTask<DateOnly?> StartAsync(IHoldOutStore? store, CancellationToken cancellationToken) =>
        store is null ? null : (await store.CurrentAsync(cancellationToken))?.Start;
}
