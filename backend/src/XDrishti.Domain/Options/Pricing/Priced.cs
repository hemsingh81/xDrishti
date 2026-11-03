namespace XDrishti.Domain.Options.Pricing;

/// <summary>Builders of <see cref="PricedValue{T}"/> (kept off the generic type).</summary>
public static class Priced
{
    public static PricedValue<T> Of<T>(T value)
        where T : struct => new(value);

    public static PricedValue<T> Unavailable<T>(UnavailableReason reason)
        where T : struct => new(reason);
}
