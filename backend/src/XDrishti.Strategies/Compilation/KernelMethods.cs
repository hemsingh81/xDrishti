using System.Reflection;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The functions of <see cref="Kernel"/> (and the two of <see cref="Math"/>) as the <see cref="MethodInfo"/>s the expression trees call, found once by name. Looking up a function that does not exist throws when this
/// type is first used, so a renamed function fails every test of the compiler, not one rarely taken path.
/// </summary>
internal static class KernelMethods
{
    public static readonly MethodInfo Not = FindKernel(nameof(Kernel.Not));

    public static readonly MethodInfo And = FindKernel(nameof(Kernel.And));

    public static readonly MethodInfo Or = FindKernel(nameof(Kernel.Or));

    public static readonly MethodInfo SameTruth = FindKernel(nameof(Kernel.SameTruth));

    public static readonly MethodInfo DifferentTruth = FindKernel(nameof(Kernel.DifferentTruth));

    public static readonly MethodInfo Less = FindKernel(nameof(Kernel.Less));

    public static readonly MethodInfo LessOrEqual = FindKernel(nameof(Kernel.LessOrEqual));

    public static readonly MethodInfo Greater = FindKernel(nameof(Kernel.Greater));

    public static readonly MethodInfo GreaterOrEqual = FindKernel(nameof(Kernel.GreaterOrEqual));

    public static readonly MethodInfo Equal = FindKernel(nameof(Kernel.Equal));

    public static readonly MethodInfo NotEqual = FindKernel(nameof(Kernel.NotEqual));

    public static readonly MethodInfo CrossesAbove = FindKernel(nameof(Kernel.CrossesAbove));

    public static readonly MethodInfo CrossesBelow = FindKernel(nameof(Kernel.CrossesBelow));

    public static readonly MethodInfo Between = FindKernel(nameof(Kernel.Between));

    public static readonly MethodInfo RunStart = FindKernel(nameof(Kernel.RunStart));

    public static readonly MethodInfo StepRising = FindKernel(nameof(Kernel.StepRising));

    public static readonly MethodInfo StepFalling = FindKernel(nameof(Kernel.StepFalling));

    public static readonly MethodInfo RunResult = FindKernel(nameof(Kernel.RunResult));

    public static readonly MethodInfo Divide = FindKernel(nameof(Kernel.Divide));

    public static readonly MethodInfo Value = FindKernel(nameof(Kernel.Value));

    public static readonly MethodInfo ValueAt = FindKernel(nameof(Kernel.ValueAt));

    public static readonly MethodInfo CloseMinute = FindKernel(nameof(Kernel.CloseMinute));

    public static readonly MethodInfo Abs = FindMath(nameof(Math.Abs), 1);

    public static readonly MethodInfo Min = FindMath(nameof(Math.Min), 2);

    public static readonly MethodInfo Max = FindMath(nameof(Math.Max), 2);

    /// <summary>The comparison function of the operator (one of the six comparisons).</summary>
    public static MethodInfo Comparison(BinaryOperator op) => op switch
    {
        BinaryOperator.Less => Less,
        BinaryOperator.LessOrEqual => LessOrEqual,
        BinaryOperator.Greater => Greater,
        BinaryOperator.GreaterOrEqual => GreaterOrEqual,
        BinaryOperator.Equal => Equal,
        BinaryOperator.NotEqual => NotEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a comparison."),
    };

    private static MethodInfo FindKernel(string name) =>
        typeof(Kernel).GetMethod(name, BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("The compiled conditions call Kernel." + name + ", which does not exist.");

    /// <summary>The overload of <see cref="Math"/> that takes <paramref name="arguments"/> doubles.</summary>
    private static MethodInfo FindMath(string name, int arguments) =>
        typeof(Math).GetMethod(name, BindingFlags.Public | BindingFlags.Static, [.. Enumerable.Repeat(typeof(double), arguments)]) ?? throw new InvalidOperationException("The compiled conditions call Math." + name + " on doubles, which does not exist.");
}
