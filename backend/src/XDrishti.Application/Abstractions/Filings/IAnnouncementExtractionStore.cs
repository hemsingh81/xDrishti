using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The identity of an extraction apart from the filing (P15-08): the model file hash, the prompt and its hash, and the schema version. A different model, prompt or schema is a different extraction.</summary>
public sealed record ExtractionKey(string ModelSha256, string PromptName, string PromptVersion, string PromptHash, string SchemaVersion);

/// <summary>An extraction to store: the state, the closed reason and the checked answer. No raw answer, no text.</summary>
public sealed record NewExtraction(Guid FilingId, ExtractionKey Key, string State, string? Reason, AnnouncementAnswer? Answer, bool Ungrounded, int LatencyMs, int? PromptTokens, int? CompletionTokens, DateTimeOffset CreatedAt);

/// <summary>Counts of stored extractions for the coverage view (no text): by month of the announcement, model, prompt, schema and state.</summary>
public sealed record ExtractionCoverageRow(string Month, string ModelSha256, string PromptName, string PromptVersion, string SchemaVersion, string State, int Count, int Ungrounded);

/// <summary><c>filings.extractions</c> (P15-08), insert-only: nothing is updated or deleted, and no decision path reads it.</summary>
public interface IAnnouncementExtractionStore
{
    /// <summary>The stored announcements broadcast in [<paramref name="from"/>, <paramref name="toExclusive"/>) that have no extraction for <paramref name="key"/>, oldest first, after skipping <paramref name="skip"/>.</summary>
    ValueTask<IReadOnlyList<Guid>> PendingAsync(DateTimeOffset from, DateTimeOffset toExclusive, ExtractionKey key, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Stores the extraction; false when that filing already has one for the key (nothing changes).</summary>
    ValueTask<bool> InsertAsync(NewExtraction extraction, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ExtractionCoverageRow>> CoverageAsync(CancellationToken cancellationToken);
}
