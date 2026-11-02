using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where the values of a feature extension are kept (P13-05): <c>research.trade_features_ext</c> behind a port. Insert-only; a row is keyed by (run key, trade key, extension, version) and a second write of it is a no-op. No decision path reads it.</summary>
public interface IExtensionFeatureStore
{
    /// <summary>Writes the rows in one transaction and returns how many were new.</summary>
    ValueTask<int> AppendAsync(IReadOnlyList<ExtensionFeatureRow> rows, DateTimeOffset createdAt, CancellationToken cancellationToken);

    /// <summary>The rows of the runs for the extension version, ordered by run key then trade key.</summary>
    ValueTask<IReadOnlyList<ExtensionFeatureRow>> ReadAsync(IReadOnlyList<string> runKeys, string version, CancellationToken cancellationToken);
}
