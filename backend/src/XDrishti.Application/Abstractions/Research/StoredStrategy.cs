namespace XDrishti.Application.Abstractions.Research;

/// <summary>A strategy family as the library lists it (spec P3-02d, D4): the id, the style copied from its first version, whether it is a built-in, when it was created, its latest version number and how many versions it has.</summary>
public sealed record StoredStrategy(string Id, string Style, bool BuiltIn, DateTimeOffset CreatedAt, int LatestVersion, int VersionCount);
