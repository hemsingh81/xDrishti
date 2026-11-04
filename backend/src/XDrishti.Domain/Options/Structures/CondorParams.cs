using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Options.Structures;

/// <summary>
/// The parameters of an <c>iron_condor</c> expression profile (spec P14-10): <c>k</c> (the short strikes, steps from the money; 4 or 6), <c>m</c> (the stop at a loss of <c>m</c> times the credit; 1 or 2) and <c>wing_steps</c> (the wings beyond the shorts; 2). The pre-registered primary is
/// <c>k</c> 6, <c>m</c> 1; the four combinations of the counted variants are N = 4. All three are always written, so one choice has one hash.
/// </summary>
public sealed record CondorParams(int K, int M, int WingSteps)
{
    public const string InvalidCode = "structure_profile.invalid";
    public const int Wings = 2;

    /// <summary>The pre-registered primary: shorts 6 steps out, stop at a loss of 1 x the credit, wings 2 steps beyond.</summary>
    public static CondorParams Primary { get; } = new(6, 1, Wings);

    /// <summary>The four counted variants (<c>k</c> 4 or 6 times <c>m</c> 1 or 2), primary first.</summary>
    public static IReadOnlyList<CondorParams> Variants { get; } = [Primary, new(6, 2, Wings), new(4, 1, Wings), new(4, 2, Wings)];

    public static Result<CondorParams> Create(int k, int m, int wingSteps) =>
        k is not (4 or 6) ? Invalid("k is 4 or 6")
        : m is not (1 or 2) ? Invalid("m is 1 or 2")
        : wingSteps != Wings ? Invalid("wing_steps is 2")
        : Result.Success(new CondorParams(k, m, wingSteps));

    public Result<ExpressionProfile> ToProfile() => ExpressionProfile.Create(Expression.IronCondor, [new("k", K), new("m", M), new("wing_steps", WingSteps)]);

    public static Result<CondorParams> FromProfile(ExpressionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind != Expression.IronCondor)
        {
            return Invalid("must be of kind iron_condor");
        }

        var values = profile.Parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return values.Count == 3 && values.TryGetValue("k", out var k) && values.TryGetValue("m", out var m) && values.TryGetValue("wing_steps", out var wings)
            && int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var kk) && int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var mm) && int.TryParse(wings, NumberStyles.None, CultureInfo.InvariantCulture, out var ww)
            ? Create(kk, mm, ww)
            : Invalid("has exactly k, m and wing_steps, whole numbers");
    }

    private static Result<CondorParams> Invalid(string rule) => Result.Failure<CondorParams>(Error.Validation(InvalidCode, "An iron_condor profile " + rule + "."));
}
