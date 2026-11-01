namespace XDrishti.Domain.Market;

/// <summary>
/// Version of the rules that turn Dhan's file into the master list (scope, canonical symbols, flags, parser tolerance). Every refresh records it.
/// Bump it whenever those rules change: the next refresh then ignores the stored <c>ETag</c>/<c>Last-Modified</c> and imports the unchanged file again.
/// </summary>
public static class RefreshRules
{
    /// <summary>2: the read also collects the option contracts of the listed underlyings (P14-01, ADR 0175), so the first refresh after it reads the whole file.</summary>
    public const int Version = 2;
}
