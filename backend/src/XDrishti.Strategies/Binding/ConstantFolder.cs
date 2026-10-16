using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// The value of a number that is known when the condition is compiled: a literal, a strategy parameter or a constant, combined by the arithmetic operators, a minus sign, <c>abs</c>, <c>min</c> and <c>max</c>. The rules
/// are those of the run time: a division by zero and any operation on a NaN are NaN. Used for the arguments of a catalog feature (<c>rsi(rsi_len * 2)</c>) and for the count of <c>rising</c> and <c>falling</c>, which
/// become part of the plan and so cannot depend on the data.
/// </summary>
internal static class ConstantFolder
{
    /// <summary>True when the node has a value at compile time (<paramref name="value"/>, which may be NaN or infinite); false when it reads the frame.</summary>
    public static bool TryFold(IBoundExpression node, out double value)
    {
        value = double.NaN;
        switch (node)
        {
            case BoundNumber number:
                value = number.Value;
                return true;
            case BoundNegate negate when TryFold(negate.Operand, out var operand):
                value = -operand;
                return true;
            case BoundAbs abs when TryFold(abs.Operand, out var operand):
                value = Math.Abs(operand);
                return true;
            case BoundArithmetic arithmetic when TryFold(arithmetic.Left, out var left) && TryFold(arithmetic.Right, out var right):
                value = arithmetic.Operator switch
                {
                    BinaryOperator.Add => left + right,
                    BinaryOperator.Subtract => left - right,
                    BinaryOperator.Multiply => left * right,
                    _ => right == 0 ? double.NaN : left / right,
                };
                return true;
            case BoundMinMax minMax when TryFold(minMax.Left, out var first) && TryFold(minMax.Right, out var second):
                value = double.IsNaN(first) || double.IsNaN(second) ? double.NaN : minMax.IsMax ? Math.Max(first, second) : Math.Min(first, second);
                return true;
            default:
                return false;
        }
    }
}
