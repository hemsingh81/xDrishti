namespace XDrishti.Domain.Options.Pricing;

/// <summary>
/// A value or the reason there is none, never both. Nothing in the pricing core returns 0, NaN or infinity for an unavailable value. A <c>default</c> instance is unavailable (<see cref="UnavailableReason.BadInput"/>).
/// </summary>
public readonly struct PricedValue<T> : IEquatable<PricedValue<T>>
    where T : struct
{
    private readonly T _value;
    private readonly UnavailableReason _reason;
    private readonly bool _available;

    internal PricedValue(T value)
    {
        _value = value;
        _reason = default;
        _available = true;
    }

    internal PricedValue(UnavailableReason reason)
    {
        _value = default;
        _reason = reason;
        _available = false;
    }

    public bool IsAvailable => _available;

    /// <summary>The value; reading it when unavailable is a caller bug and throws (check <see cref="IsAvailable"/> or use <see cref="TryGet"/>).</summary>
    public T Value => _available ? _value : throw new InvalidOperationException("The value is unavailable: " + _reason.Label());

    /// <summary>The reason there is no value; null when there is one.</summary>
    public UnavailableReason? Reason => _available ? null : _reason;

    public bool TryGet(out T value)
    {
        value = _value;
        return _available;
    }

    public bool Equals(PricedValue<T> other) => _available == other._available && (_available ? EqualityComparer<T>.Default.Equals(_value, other._value) : _reason == other._reason);

    public override bool Equals(object? obj) => obj is PricedValue<T> other && Equals(other);

    public override int GetHashCode() => _available ? HashCode.Combine(true, _value) : HashCode.Combine(false, _reason);

    public static bool operator ==(PricedValue<T> left, PricedValue<T> right) => left.Equals(right);

    public static bool operator !=(PricedValue<T> left, PricedValue<T> right) => !left.Equals(right);
}
