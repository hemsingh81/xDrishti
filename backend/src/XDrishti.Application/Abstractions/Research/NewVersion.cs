namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// A version to append (spec P3-02d, D4). The caller has read and validated the text with the strategy file library and hands over the canonical text and its hash; the store checks the hash against the canonical text and the sizes, nothing more.
/// </summary>
/// <param name="StrategyId">The family; created when it is new.</param>
/// <param name="Style"><c>intraday</c> or <c>swing</c>; used only when the family is created.</param>
/// <param name="SourceText">The text as posted, at most 262,144 bytes.</param>
/// <param name="CanonicalJson">The canonical text.</param>
/// <param name="Hash">The SHA-256 of the canonical text, 64 lowercase hex characters.</param>
/// <param name="ParentHash">The version this one was made from; null means the previous latest version of the family (none for the first).</param>
/// <param name="Note">A note of at most 500 characters.</param>
/// <param name="ExpectedVersion">The number the caller expects this version to get; a different next number is a conflict.</param>
/// <param name="SchemaVersion">The schema of the model the text is written in (1).</param>
/// <param name="Seed">The built-ins' own writer: it may create a built-in family and append to one. Everyone else may do neither.</param>
public sealed record NewVersion(
    string StrategyId,
    string Style,
    string SourceText,
    string CanonicalJson,
    string Hash,
    string? ParentHash = null,
    string? Note = null,
    int? ExpectedVersion = null,
    bool Seed = false,
    int SchemaVersion = 1);
