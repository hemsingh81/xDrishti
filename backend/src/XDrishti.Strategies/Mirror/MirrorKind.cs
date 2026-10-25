namespace XDrishti.Strategies.Mirror;

/// <summary>
/// The mirror type of an expression (spec P3-02e): how its value on the inverted market <c>p' = K - p</c> relates to its value on the market. <see cref="Level"/>: a price (<c>K - x</c>). <see cref="Signed"/>: <c>-x</c>.
/// <see cref="Osc"/>: <c>100 - x</c>. <see cref="Plain"/>: <c>x</c>. <see cref="Scalar"/>: a literal or a parameter, which has no type of its own and takes the one of the other side of a comparison. <see cref="Bool"/>: a condition.
/// </summary>
internal enum MirrorKind
{
    Scalar,
    Plain,
    Level,
    Signed,
    Osc,
    Bool,
}
