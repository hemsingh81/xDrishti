namespace XDrishti.Strategies.Syntax;

/// <summary>The kinds of token the lexer produces. The last four are not part of the language: they stand for something wrong or missing, so that the parser can go on and find the next problem.</summary>
internal enum Tk
{
    None = 0,
    Number,
    String,
    Identifier,
    And,
    Or,
    Not,
    True,
    False,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Dot,
    Assign,
    Plus,
    Minus,
    Star,
    Slash,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,

    /// <summary>A character that is not part of the language.</summary>
    Unknown,

    /// <summary>A double quote with no closing quote on its line.</summary>
    UnterminatedString,

    /// <summary>A number glued to letters, digits or underscores.</summary>
    BadNumber,

    /// <summary>A value that is missing or was unreadable (already reported); the grammar accepts it wherever a value, a name, a string or a whole number may stand.</summary>
    Missing,
}
