using System.Globalization;
using System.Text.Json;

namespace XDrishti.Domain.Platform.Services;

/// <summary>
/// One parameter a service accepts (the schema of ADR 0018 §1): its kind, the bounds of a number and the default. Values are only ever compared with the bounds, never executed or
/// interpolated. No parameter is a secret.
/// </summary>
public sealed record ParameterDefinition(string Name, string Label, ParameterKind Kind, double Min, double Max, JsonElement Default, string? Description = null, string? Unit = null)
{
    public static ParameterDefinition Whole(string name, string label, int min, int max, int @default, string? description = null, string? unit = null) =>
        new(name, label, ParameterKind.Whole, min, max, JsonSerializer.SerializeToElement(@default), description, unit);

    public static ParameterDefinition Fractional(string name, string label, double min, double max, double @default, string? description = null, string? unit = null) =>
        new(name, label, ParameterKind.Fractional, min, max, JsonSerializer.SerializeToElement(@default), description, unit);

    public static ParameterDefinition Switch(string name, string label, bool @default, string? description = null) =>
        new(name, label, ParameterKind.Switch, 0, 1, JsonSerializer.SerializeToElement(@default), description);

    /// <summary>Whether the value has the kind and lies within the bounds; <paramref name="problem"/> says why not.</summary>
    public bool Accepts(JsonElement value, out string problem)
    {
        problem = string.Empty;
        switch (Kind)
        {
            case ParameterKind.Switch:
                if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return true;
                }

                problem = "Use true or false.";
                return false;
            case ParameterKind.Whole:
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole) && whole >= Min && whole <= Max)
                {
                    return true;
                }

                problem = $"Use a whole number from {Min.ToString("0.##", CultureInfo.InvariantCulture)} to {Max.ToString("0.##", CultureInfo.InvariantCulture)}.";
                return false;
            default:
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number >= Min && number <= Max)
                {
                    return true;
                }

                problem = $"Use a number from {Min.ToString("0.##", CultureInfo.InvariantCulture)} to {Max.ToString("0.##", CultureInfo.InvariantCulture)}.";
                return false;
        }
    }
}
