using System.Linq.Expressions;
using System.Reflection;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>What an audit of an expression tree found: every rule it saw broken, and the functions the tree calls (to prove the audit looked at something).</summary>
internal sealed record AuditReport(IReadOnlyList<string> Violations, IReadOnlySet<string> Methods);

/// <summary>
/// Reads an expression tree the compiler made and says what it can do — the structural half of "a compiled condition cannot look ahead", which no property test over the feed can see. It proves three things: <b>no negative
/// lag</b> (the lag handed to <c>Kernel.ValueAt</c> is a sum of non-negative whole numbers and the loop counter of a run, never a subtraction, a negation or a negative constant, so the future cannot be read at all: the
/// frame throws for a lag below 0), an <b>additive shape</b>, and <b>kernel-only access</b> (a condition may call only the public functions of <see cref="Kernel"/> and <c>Math.Abs/Min/Max</c> on doubles; may touch the
/// frame only through <c>Kernel.Value</c>, <c>ValueAt</c> and <c>CloseMinute</c>, handing them the frame it was given; may hold no object but numbers; and may use no member access, construction, invocation or indexing other
/// than the handle array). It does <b>not</b> prove that a lag is exactly the one written, or that the clock is the right one: a tree that reads a feature one bar closer than written, or <c>time</c> a minute ahead, passes
/// it. That is held by the interpreter oracle and the hand examples, and <c>InterpreterOracleTests</c> carries a mutant control that shows the oracle corpus notices such a tree.
/// </summary>
internal static class ExpressionAudit
{
    /// <summary>The node types the compiler uses: constants, the two parameters, calls, arithmetic on doubles, the handle array, blocks and the loop of a run (with its empty void ends), assignments and the conditions of an <c>and</c>/<c>or</c>.</summary>
    private static readonly HashSet<ExpressionType> Vocabulary =
    [
        ExpressionType.Constant, ExpressionType.Parameter, ExpressionType.Call, ExpressionType.Add, ExpressionType.Subtract, ExpressionType.Multiply, ExpressionType.Negate, ExpressionType.ArrayIndex,
        ExpressionType.Block, ExpressionType.Loop, ExpressionType.Label, ExpressionType.Goto, ExpressionType.Assign, ExpressionType.Conditional, ExpressionType.Equal, ExpressionType.GreaterThan,
        ExpressionType.PreIncrementAssign, ExpressionType.Or, ExpressionType.Default,
    ];

    /// <summary>The functions a condition may call: every public static function of the kernel, and the three <see cref="Math"/> functions on doubles.</summary>
    private static readonly HashSet<MethodInfo> AllowedMethods =
    [
        .. typeof(Kernel).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
        typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!,
        typeof(Math).GetMethod(nameof(Math.Min), [typeof(double), typeof(double)])!,
        typeof(Math).GetMethod(nameof(Math.Max), [typeof(double), typeof(double)])!,
    ];

    /// <summary>The only kernel functions that take the frame: the value as of the last closed bar, the value some closed bars ago, and the clock.</summary>
    private static readonly HashSet<string> FrameDoors = [nameof(Kernel.Value), nameof(Kernel.ValueAt), nameof(Kernel.CloseMinute)];

    public static AuditReport Of(LambdaExpression lambda)
    {
        var auditor = new Auditor(lambda.Parameters[0]);
        auditor.Visit(lambda.Body);
        return new AuditReport(auditor.Violations, auditor.Methods);
    }

    private sealed class Auditor(ParameterExpression frame) : ExpressionVisitor
    {
        public List<string> Violations { get; } = [];

        public HashSet<string> Methods { get; } = [];

        public override Expression? Visit(Expression? node)
        {
            if (node is not null && !Vocabulary.Contains(node.NodeType))
            {
                Violations.Add("a " + node.NodeType + " node (" + node + ")");
            }

            return base.Visit(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is not (int or double))
            {
                Violations.Add("a constant that is not a number: " + (node.Value?.GetType().Name ?? "null"));
            }

            return base.VisitConstant(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Methods.Add(node.Method.Name);
            if (!AllowedMethods.Contains(node.Method))
            {
                Violations.Add("a call of " + node.Method.DeclaringType?.Name + "." + node.Method.Name + ", which is not a function of the kernel");
            }
            else if (node.Method.GetParameters().Any(p => p.ParameterType == typeof(FeatureFrame)) && !FrameDoors.Contains(node.Method.Name))
            {
                Violations.Add("Kernel." + node.Method.Name + " takes the frame but is not one of the three doors");
            }

            if (node.Method.GetParameters().Any(p => p.ParameterType == typeof(FeatureFrame)) && node.Arguments[0] != frame)
            {
                Violations.Add("Kernel." + node.Method.Name + " is not given the frame the condition was given");
            }

            if (node.Method.Name == nameof(Kernel.ValueAt) && node.Method.DeclaringType == typeof(Kernel) && !IsNeverNegative(node.Arguments[2]))
            {
                Violations.Add("a lag that can be below zero: " + node.Arguments[2]);
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>A sum of whole numbers that are not negative and of the counter of a run (which starts at 1 and only counts up): the one shape of a lag the compiler builds, and one that is never below zero.</summary>
        private static bool IsNeverNegative(Expression lag) => lag switch
        {
            ConstantExpression { Value: int value } => value >= 0,
            ParameterExpression { Type: var type } => type == typeof(int),
            BinaryExpression { NodeType: ExpressionType.Add } sum => IsNeverNegative(sum.Left) && IsNeverNegative(sum.Right),
            _ => false,
        };
    }
}
