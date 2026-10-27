namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The catalog of the views the model may read: the manifest joined to the live database. Reads under the read-only role.</summary>
public interface IAiCatalog
{
    /// <summary>The views whose name starts with any of the prefixes (all when the list is empty).</summary>
    Task<IReadOnlyList<CatalogView>> ListAsync(IReadOnlyList<string> prefixes, CancellationToken cancellationToken);

    /// <summary>The view of this name (a name the manifest lists), or null.</summary>
    Task<CatalogTable?> DescribeAsync(string name, CancellationToken cancellationToken);
}
