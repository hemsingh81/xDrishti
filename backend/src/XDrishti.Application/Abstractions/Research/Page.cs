namespace XDrishti.Application.Abstractions.Research;

/// <summary>A page of a keyset-paged read: the items and the opaque cursor of the next page, null at the end.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
