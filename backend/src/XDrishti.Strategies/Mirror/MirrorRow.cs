namespace XDrishti.Strategies.Mirror;

/// <summary>One row of the mirror table: the type of the name, the name it mirrors to and whether the property holds exactly under <c>p' = K - p</c> (the "exact class", D4).</summary>
internal sealed record MirrorRow(MirrorKind Kind, string Mirrored, bool Exact = true);
