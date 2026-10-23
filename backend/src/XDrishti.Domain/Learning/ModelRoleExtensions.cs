namespace XDrishti.Domain.Learning;

/// <summary>The stored text of a <see cref="ModelRole"/> (P6-07).</summary>
public static class ModelRoleExtensions
{
    /// <summary>The lower-case code kept in the database and shown in the API.</summary>
    public static string Code(this ModelRole role) => role switch
    {
        ModelRole.Challenger => "challenger",
        ModelRole.Champion => "champion",
        ModelRole.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>The role of a code, or null when it is none.</summary>
    public static ModelRole? Parse(string? code) => code switch
    {
        "challenger" => ModelRole.Challenger,
        "champion" => ModelRole.Champion,
        "retired" => ModelRole.Retired,
        _ => null,
    };
}
