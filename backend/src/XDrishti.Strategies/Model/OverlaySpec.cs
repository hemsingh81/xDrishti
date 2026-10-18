namespace XDrishti.Strategies.Model;

/// <summary>An optional overlay exit applied to every candidate policy, and its parameters. Equal by value.</summary>
public sealed record OverlaySpec(OverlayKind Kind, IReadOnlyList<ExitParameter> Parameters)
{
    /// <inheritdoc />
    public bool Equals(OverlaySpec? other) => other is not null && Kind == other.Kind && ListEquality.Equal(Parameters, other.Parameters);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, ListEquality.Hash(Parameters));
}
