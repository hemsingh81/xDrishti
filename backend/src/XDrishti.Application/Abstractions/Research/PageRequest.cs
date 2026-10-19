namespace XDrishti.Application.Abstractions.Research;

/// <summary>A page asked for by cursor (spec P3-10, D7): the size (1 to <see cref="MaxSize"/>) and the opaque cursor the previous page gave; null for the first page.</summary>
public sealed record PageRequest(int Size, string? Cursor = null)
{
    /// <summary>The largest page.</summary>
    public const int MaxSize = 1000;

    /// <summary>True for a size from 1 to <see cref="MaxSize"/>.</summary>
    public bool IsSizeValid => Size is >= 1 and <= MaxSize;
}
