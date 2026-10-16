using Superpower;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace XDrishti.Strategies.Syntax;

/// <summary>
/// The tokenizer of the rules language, built with Superpower. It never fails: whatever it cannot read becomes a token of an error kind (<see cref="Tk.Unknown"/>, <see cref="Tk.UnterminatedString"/>,
/// <see cref="Tk.BadNumber"/>) with its own text, so every wrong place can be reported and the lexing goes on. Only ASCII is part of the language outside strings: a full-width <c>＞</c>, a no-break space or an
/// accented letter is an <see cref="Tk.Unknown"/> token, never silently accepted as its look-alike.
/// </summary>
internal static class Lexer
{
    private static readonly TextParser<TextSpan> Word = Span.WithAll(IsWordPart);

    private static readonly TextParser<Unit> Digits = Character.Matching(IsDigit, "digit").AtLeastOnce().Value(Unit.Value);

    private static readonly TextParser<Unit> Fraction = Character.EqualTo('.').IgnoreThen(Digits).Try();

    private static readonly TextParser<Unit> Exponent = Character.EqualTo('e').IgnoreThen(Character.In('+', '-').OptionalOrDefault()).IgnoreThen(Digits).Try();

    private static readonly TextParser<Unit> NumberText = Digits.Then(_ => Fraction.OptionalOrDefault()).Then(_ => Exponent.OptionalOrDefault());

    private static readonly TextParser<Unit> NumberWithTail = NumberText.IgnoreThen(Character.Matching(IsWordPart, "letter, digit or underscore").AtLeastOnce().Value(Unit.Value));

    private static readonly TextParser<Unit> StringBody = Character.ExceptIn('"', '\r', '\n').IgnoreMany().Value(Unit.Value);

    private static readonly TextParser<Unit> ClosedString = Character.EqualTo('"').IgnoreThen(StringBody).IgnoreThen(Character.EqualTo('"')).Value(Unit.Value);

    private static readonly TextParser<Unit> OpenString = Character.EqualTo('"').IgnoreThen(StringBody).Value(Unit.Value);

    private static readonly TextParser<Unit> AnyCodePoint =
        Character.Matching(char.IsHighSurrogate, "high surrogate").IgnoreThen(Character.Matching(char.IsLowSurrogate, "low surrogate")).Try().Value(Unit.Value)
            .Or(Character.AnyChar.Value(Unit.Value));

    /// <summary>The tokenizer. It holds no state, so one instance serves every thread.</summary>
    public static readonly Tokenizer<Tk> Instance = new TokenizerBuilder<Tk>()
        .Ignore(Character.In(' ', '\t', '\r', '\n').AtLeastOnce())
        .Match(ClosedString, Tk.String)
        .Match(OpenString, Tk.UnterminatedString)
        .Match(ForeignString('\'', '\''), Tk.Unknown)
        .Match(ForeignString('\u2018', '\u2019'), Tk.Unknown)
        .Match(ForeignString('\u201C', '\u201D'), Tk.Unknown)
        .Match(ForeignString('\u201E', '\u201C', '\u201D'), Tk.Unknown)
        .Match(ForeignString('\u00AB', '\u00BB'), Tk.Unknown)
        .Match(NumberWithTail, Tk.BadNumber)
        .Match(NumberText, Tk.Number)
        .Match(Keyword("and"), Tk.And)
        .Match(Keyword("or"), Tk.Or)
        .Match(Keyword("not"), Tk.Not)
        .Match(Keyword("true"), Tk.True)
        .Match(Keyword("false"), Tk.False)
        .Match(Word, Tk.Identifier)
        .Match(Character.EqualTo('('), Tk.LeftParen)
        .Match(Character.EqualTo(')'), Tk.RightParen)
        .Match(Character.EqualTo('['), Tk.LeftBracket)
        .Match(Character.EqualTo(']'), Tk.RightBracket)
        .Match(Character.EqualTo(','), Tk.Comma)
        .Match(Character.EqualTo('.'), Tk.Dot)
        .Match(Span.EqualTo("<="), Tk.LessOrEqual)
        .Match(Span.EqualTo(">="), Tk.GreaterOrEqual)
        .Match(Span.EqualTo("=="), Tk.Equal)
        .Match(Span.EqualTo("!="), Tk.NotEqual)
        .Match(Character.EqualTo('<'), Tk.Less)
        .Match(Character.EqualTo('>'), Tk.Greater)
        .Match(Character.EqualTo('='), Tk.Assign)
        .Match(Character.EqualTo('+'), Tk.Plus)
        .Match(Character.EqualTo('-'), Tk.Minus)
        .Match(Character.EqualTo('*'), Tk.Star)
        .Match(Character.EqualTo('/'), Tk.Slash)
        .Match(Span.EqualTo("&&"), Tk.Unknown)
        .Match(Span.EqualTo("||"), Tk.Unknown)
        .Match(AnyCodePoint, Tk.Unknown)
        .Build();

    /// <summary>A string in some other kind of quotes (single, typographic, guillemets) on one line: read as one unknown token, so that it is one problem and not one per character.</summary>
    private static TextParser<Unit> ForeignString(char open, params char[] closers) =>
        Character.EqualTo(open).IgnoreThen(Character.ExceptIn([.. closers, '\r', '\n']).IgnoreMany()).IgnoreThen(Character.In(closers)).Value(Unit.Value);

    private static TextParser<Unit> Keyword(string text) => Word.Where(w => w.EqualsValue(text)).Value(Unit.Value);

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsWordPart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
}
