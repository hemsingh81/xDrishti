namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// A service as the code knows it (ADR 0018 §1): registered by the module that owns the work. <see cref="RunsAs"/> says where it runs (xd-api today). <see cref="AllowedTypes"/> are the
/// schedule types the owner may choose, <see cref="Defaults"/> what the service runs on without a stored row, <see cref="Parameters"/> the schema of its parameters.
/// </summary>
public sealed record ServiceDefinition(
    string Id,
    string Name,
    string Description,
    string RunsAs,
    IReadOnlyList<ScheduleType> AllowedTypes,
    ServiceSettings Defaults,
    IReadOnlyList<ParameterDefinition> Parameters,
    ServiceConstraints Constraints)
{
    public const int MaxIdLength = 64;

    public ParameterDefinition? FindParameter(string name) => Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>The parameters with every missing value filled from the schema's defaults.</summary>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> WithDefaults(IReadOnlyDictionary<string, System.Text.Json.JsonElement> given) =>
        Parameters.ToDictionary(p => p.Name, p => given.TryGetValue(p.Name, out var v) ? v : p.Default, StringComparer.Ordinal);
}
