namespace XDrishti.Domain.Market;

/// <summary>
/// Version of the rules that turn Dhan's file into the master list (scope, canonical symbols, flags, parser tolerance). Every refresh records it.
/// Bump it whenever those rules change: the next refresh then ignores the stored <c>ETag</c>/<c>Last-Modified</c> and imports the unchanged file again.
/// </summary>
public static class RefreshRules
{
    public const int Version = 1;
}
