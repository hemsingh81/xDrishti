namespace XDrishti.Strategies.Model;

/// <summary>One named parameter of an exit policy or overlay: one value for a scalar, one to ten candidate values for a list (<c>rr: [1.5, 2, 3]</c>). Equal by value.</summary>
public sealed record ExitParameter(string Name, IReadOnlyList<Quantity> Values)
{
    /// <inheritdoc />
    public bool Equals(ExitParameter? other) => other is not null && Name == other.Name && ListEquality.Equal(Values, other.Values);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, ListEquality.Hash(Values));
}
