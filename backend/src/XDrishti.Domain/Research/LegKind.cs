namespace XDrishti.Domain.Research;

/// <summary>What a leg of a trade did (P4-01, D7). Stored by name, never renamed.</summary>
public enum LegKind
{
    /// <summary>The first fill of the position.</summary>
    Entry,

    /// <summary>A later fill that adds to the position (P4-04).</summary>
    Add,

    /// <summary>A fill that takes shares out of the position, the last one included.</summary>
    Exit,
}
