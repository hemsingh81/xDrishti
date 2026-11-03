namespace XDrishti.Domain.Pairs;

/// <summary>
/// A value or the reason there is none, never both. Nothing in the pairs core returns 0, NaN or infinity for an unavailable value. A <c>default</c> instance is unavailable (<see cref="PairsReason.BadInput"/>).
/// </summary>
public readonly struct PairsResult<T> : IEquatable<PairsResult<T>>
    where T : notnull
{
    private readonly T? _value;
    private readonly PairsReason _reason;
    private readonly bool _available;

    internal PairsResult(T value)
    {
        _value = value;
        _reason = default;
        _available = true;
    }

    internal PairsResult(PairsReason reason)
    {
        _value = default;
        _reason = reason;
        _available = false;
    }

    public bool IsAvailable => _available;

    /// <summary>The value; reading it when unavailable is a caller bug and throws (check <see cref="IsAvailable"/> or use <see cref="TryGet"/>).</summary>
    public T Value => _available ? _value! : throw new InvalidOperationException("The value is unavailable: " + _reason.Label());

    /// <summary>The reason there is no value; null when there is one.</summary>
    public PairsReason? Reason => _available ? null : _reason;

    public bool TryGet(out T value)
    {
        value = _available ? _value! : default!;
        return _available;
    }

    public bool Equals(PairsResult<T> other) => _available == other._available && (_available ? EqualityComparer<T>.Default.Equals(_value, other._value) : _reason == other._reason);

    public override bool Equals(object? obj) => obj is PairsResult<T> other && Equals(other);

    public override int GetHashCode() => _available ? HashCode.Combine(true, _value) : HashCode.Combine(false, _reason);

    public static bool operator ==(PairsResult<T> left, PairsResult<T> right) => left.Equals(right);

    public static bool operator !=(PairsResult<T> left, PairsResult<T> right) => !left.Equals(right);
}
