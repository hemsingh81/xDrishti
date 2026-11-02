using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where the record of an extension extraction is kept (P15-05): <c>research.extension_extractions</c> behind a port. Reading only: the record is written together with the rows by <see cref="IExtensionFeatureStore.AppendWithRecordAsync"/>, insert-only, one per (run key, extension, version). No decision path reads it.</summary>
public interface IExtensionExtractionStore
{
    /// <summary>The record of the extraction, or null when there is none.</summary>
    ValueTask<ExtensionExtractionRecord?> FindAsync(string runKey, string version, CancellationToken cancellationToken);
}
