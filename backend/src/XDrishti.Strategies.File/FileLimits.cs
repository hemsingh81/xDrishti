namespace XDrishti.Strategies.File;

/// <summary>The hard limits of the reader (P3-02c D3), all checked before the memory they guard is used. Values are the defaults of the spec.</summary>
public sealed record FileLimits
{
    /// <summary>The most bytes of text (UTF-8), checked before parsing.</summary>
    public int MaxBytes { get; init; } = 256 * 1024;

    /// <summary>The deepest nesting of mappings and sequences.</summary>
    public int MaxDepth { get; init; } = 12;

    /// <summary>The most nodes (mappings, sequences and scalars) in the document.</summary>
    public int MaxNodes { get; init; } = 5_000;

    /// <summary>The most characters in one scalar.</summary>
    public int MaxScalarLength { get; init; } = 4_096;

    /// <summary>The most keys in one mapping.</summary>
    public int MaxMappingKeys { get; init; } = 64;

    /// <summary>The most items in one sequence.</summary>
    public int MaxSequenceItems { get; init; } = 100;
}
