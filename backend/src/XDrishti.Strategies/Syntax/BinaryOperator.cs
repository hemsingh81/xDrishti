namespace XDrishti.Strategies.Syntax;

/// <summary>The infix operators, from loosest to tightest: <c>or</c>, <c>and</c>, the six comparisons, <c>+ -</c>, <c>* /</c>.</summary>
public enum BinaryOperator
{
    /// <summary><c>a or b</c>.</summary>
    Or = 0,

    /// <summary><c>a and b</c>.</summary>
    And = 1,

    /// <summary><c>a &lt; b</c>.</summary>
    Less = 2,

    /// <summary><c>a &lt;= b</c>.</summary>
    LessOrEqual = 3,

    /// <summary><c>a &gt; b</c>.</summary>
    Greater = 4,

    /// <summary><c>a &gt;= b</c>.</summary>
    GreaterOrEqual = 5,

    /// <summary><c>a == b</c>.</summary>
    Equal = 6,

    /// <summary><c>a != b</c>.</summary>
    NotEqual = 7,

    /// <summary><c>a + b</c>.</summary>
    Add = 8,

    /// <summary><c>a - b</c>.</summary>
    Subtract = 9,

    /// <summary><c>a * b</c>.</summary>
    Multiply = 10,

    /// <summary><c>a / b</c>.</summary>
    Divide = 11,
}
