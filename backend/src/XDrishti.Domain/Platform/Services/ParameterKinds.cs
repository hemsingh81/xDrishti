namespace XDrishti.Domain.Platform.Services;

/// <summary>The wire codes of <see cref="ParameterKind"/>.</summary>
public static class ParameterKinds
{
    public static string ToCode(this ParameterKind kind) => kind switch
    {
        ParameterKind.Whole => "integer",
        ParameterKind.Fractional => "number",
        _ => "boolean",
    };
}
