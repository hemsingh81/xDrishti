namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The identity of one trial (P13-05): the canonical text, its SHA-256 (the ledger's strategy hash) and the deterministic ledger run id.</summary>
/// <param name="Family">The ledger family, <c>ext:&lt;version&gt;</c>.</param>
/// <param name="Text">The canonical trial text.</param>
/// <param name="Hash">SHA-256 of the text, lower-case hex (64 characters).</param>
/// <param name="RunId">The ledger run id: the same trial on the same data window is the same id, so a re-run appends nothing.</param>
public sealed record ExtensionTrialId(string Family, string Text, string Hash, Guid RunId);
