namespace XDrishti.Strategies.Model;

/// <summary>One candidate exit policy and its parameters. Equal by value.</summary>
public sealed record ExitSpec(ExitPolicy Policy, IReadOnlyList<ExitParameter> Parameters)
{
    /// <inheritdoc />
    public bool Equals(ExitSpec? other) => other is not null && Policy == other.Policy && ListEquality.Equal(Parameters, other.Parameters);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Policy, ListEquality.Hash(Parameters));
}
