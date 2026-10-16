namespace XDrishti.Strategies.Syntax;

/// <summary>Facts about token kinds that the lexical pass, the nesting check and the recovery share.</summary>
internal static class TokenRules
{
    /// <summary>A token a value can end with: after it comes an operator (or the end), never another value.</summary>
    public static bool EndsOperand(Tk kind) =>
        kind is Tk.Number or Tk.String or Tk.Identifier or Tk.True or Tk.False or Tk.RightParen or Tk.RightBracket or Tk.Missing;

    /// <summary>A token that cannot end the condition and needs a value (or a name) after it.</summary>
    public static bool NeedsOperandAfter(Tk kind) =>
        kind is Tk.None or Tk.And or Tk.Or or Tk.Not or Tk.LeftParen or Tk.LeftBracket or Tk.Comma or Tk.Dot or Tk.Assign
            or Tk.Plus or Tk.Minus or Tk.Star or Tk.Slash or Tk.Less or Tk.LessOrEqual or Tk.Greater or Tk.GreaterOrEqual or Tk.Equal or Tk.NotEqual;

    public static bool IsComparison(Tk kind) => kind is Tk.Less or Tk.LessOrEqual or Tk.Greater or Tk.GreaterOrEqual or Tk.Equal or Tk.NotEqual;

    /// <summary>An operator written between two values (the minus sign counts only when it follows a value).</summary>
    public static bool IsInfix(Tk kind) => kind is Tk.And or Tk.Or or Tk.Plus or Tk.Minus or Tk.Star or Tk.Slash || IsComparison(kind);

    public static bool StartsOperand(Tk kind) =>
        kind is Tk.Number or Tk.String or Tk.Identifier or Tk.True or Tk.False or Tk.LeftParen or Tk.Missing;
}
