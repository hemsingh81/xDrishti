using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Research;

/// <summary>
/// A small strict JSON reader for the charge sheet (ADR 0042, D4). The Domain research namespace may not use <c>System.Text.Json</c> (its canonical texts are hand-written: the bytes are ours and not a framework version's), and this reader
/// does what the sheet needs and what the framework reader does not by default: it keeps <b>every</b> member of an object, repeated names included, so that the sheet reader can refuse a repeated name instead of letting the last value win silently;
/// it keeps a number as its text, so that a rate is a decimal and never passes through a double; and it reads <c>NaN</c> and <c>Infinity</c> as a non-finite number token, so that the field is named in the error. Nesting is limited to 8 levels.
/// </summary>
internal static class MiniJson
{
    private const int MaxDepth = 8;

    /// <summary>What a node is.</summary>
    public enum Kind
    {
        Object,
        Array,
        String,
        Number,
        NonFinite,
        Null,
        Bool,
    }

    /// <summary>Reads the text; a problem comes back as a message and a null node.</summary>
    public static Node? Parse(string text, out string? problem)
    {
        var reader = new Reader(text);
        try
        {
            reader.SkipSpace();
            var node = reader.Value(0);
            reader.SkipSpace();
            if (!reader.AtEnd)
            {
                throw new FormatException("unexpected text after the value at position " + reader.Position.ToString(CultureInfo.InvariantCulture));
            }

            problem = null;
            return node;
        }
        catch (FormatException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    /// <summary>One value of the text.</summary>
    public sealed class Node
    {
        public Node(Kind kind, string text = "")
        {
            Kind = kind;
            Text = text;
        }

        public Kind Kind { get; }

        /// <summary>The string value, or the text of the number.</summary>
        public string Text { get; }

        /// <summary>The members of an object in the order of the text, every one of them.</summary>
        public List<KeyValuePair<string, Node>> Members { get; } = [];

        /// <summary>The items of an array.</summary>
        public List<Node> Items { get; } = [];
    }

    private sealed class Reader(string text)
    {
        private int _position;

        public bool AtEnd => _position >= text.Length;

        public int Position => _position;

        public void SkipSpace()
        {
            while (_position < text.Length && text[_position] is ' ' or '\t' or '\n' or '\r')
            {
                _position++;
            }
        }

        public Node Value(int depth)
        {
            if (AtEnd)
            {
                throw new FormatException("the text ends where a value is expected");
            }

            if (depth > MaxDepth)
            {
                throw new FormatException("nested deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels");
            }

            var c = text[_position];
            switch (c)
            {
                case '{':
                    return Object(depth);
                case '[':
                    return Array(depth);
                case '"':
                    return new Node(Kind.String, String());
                case 't':
                    Word("true");
                    return new Node(Kind.Bool, "true");
                case 'f':
                    Word("false");
                    return new Node(Kind.Bool, "false");
                case 'n':
                    Word("null");
                    return new Node(Kind.Null);
                case 'N':
                    Word("NaN");
                    return new Node(Kind.NonFinite, "NaN");
                case 'I':
                    Word("Infinity");
                    return new Node(Kind.NonFinite, "Infinity");
                default:
                    if (c == '-' && _position + 1 < text.Length && text[_position + 1] == 'I')
                    {
                        _position++;
                        Word("Infinity");
                        return new Node(Kind.NonFinite, "-Infinity");
                    }

                    return Number();
            }
        }

        private Node Object(int depth)
        {
            var node = new Node(Kind.Object);
            _position++;
            SkipSpace();
            if (!AtEnd && text[_position] == '}')
            {
                _position++;
                return node;
            }

            while (true)
            {
                SkipSpace();
                if (AtEnd || text[_position] != '"')
                {
                    throw new FormatException("a property name is expected at position " + _position.ToString(CultureInfo.InvariantCulture));
                }

                var name = String();
                SkipSpace();
                Expect(':');
                SkipSpace();
                node.Members.Add(new KeyValuePair<string, Node>(name, Value(depth + 1)));
                SkipSpace();
                if (AtEnd)
                {
                    throw new FormatException("the text ends inside an object");
                }

                if (text[_position] == ',')
                {
                    _position++;
                    continue;
                }

                Expect('}');
                return node;
            }
        }

        private Node Array(int depth)
        {
            var node = new Node(Kind.Array);
            _position++;
            SkipSpace();
            if (!AtEnd && text[_position] == ']')
            {
                _position++;
                return node;
            }

            while (true)
            {
                SkipSpace();
                node.Items.Add(Value(depth + 1));
                SkipSpace();
                if (AtEnd)
                {
                    throw new FormatException("the text ends inside an array");
                }

                if (text[_position] == ',')
                {
                    _position++;
                    continue;
                }

                Expect(']');
                return node;
            }
        }

        private string String()
        {
            var value = new StringBuilder();
            _position++;
            while (true)
            {
                if (AtEnd)
                {
                    throw new FormatException("the text ends inside a string");
                }

                var c = text[_position++];
                if (c == '"')
                {
                    return value.ToString();
                }

                if (c < ' ')
                {
                    throw new FormatException("a control character inside a string at position " + (_position - 1).ToString(CultureInfo.InvariantCulture));
                }

                if (c != '\\')
                {
                    value.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw new FormatException("the text ends inside an escape");
                }

                var e = text[_position++];
                switch (e)
                {
                    case '"':
                    case '\\':
                    case '/':
                        value.Append(e);
                        break;
                    case 'b':
                        value.Append('\b');
                        break;
                    case 'f':
                        value.Append('\f');
                        break;
                    case 'n':
                        value.Append('\n');
                        break;
                    case 'r':
                        value.Append('\r');
                        break;
                    case 't':
                        value.Append('\t');
                        break;
                    case 'u':
                        if (_position + 4 > text.Length || !int.TryParse(text.AsSpan(_position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            throw new FormatException("a bad \\u escape at position " + _position.ToString(CultureInfo.InvariantCulture));
                        }

                        value.Append((char)code);
                        _position += 4;
                        break;
                    default:
                        throw new FormatException("a bad escape at position " + (_position - 1).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private Node Number()
        {
            var start = _position;
            if (text[_position] == '-')
            {
                _position++;
            }

            var digits = 0;
            while (_position < text.Length && char.IsAsciiDigit(text[_position]))
            {
                _position++;
                digits++;
            }

            if (digits == 0)
            {
                throw new FormatException("a value is expected at position " + start.ToString(CultureInfo.InvariantCulture));
            }

            if (_position < text.Length && text[_position] == '.')
            {
                _position++;
                var fraction = 0;
                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    _position++;
                    fraction++;
                }

                if (fraction == 0)
                {
                    throw new FormatException("a digit is expected after the decimal point at position " + _position.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (_position < text.Length && text[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < text.Length && text[_position] is '+' or '-')
                {
                    _position++;
                }

                var exponent = 0;
                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    _position++;
                    exponent++;
                }

                if (exponent == 0)
                {
                    throw new FormatException("a digit is expected in the exponent at position " + _position.ToString(CultureInfo.InvariantCulture));
                }
            }

            return new Node(Kind.Number, text[start.._position]);
        }

        private void Word(string word)
        {
            if (string.CompareOrdinal(text, _position, word, 0, word.Length) != 0)
            {
                throw new FormatException("an unknown word at position " + _position.ToString(CultureInfo.InvariantCulture));
            }

            _position += word.Length;
        }

        private void Expect(char c)
        {
            if (AtEnd || text[_position] != c)
            {
                throw new FormatException("'" + c + "' is expected at position " + _position.ToString(CultureInfo.InvariantCulture));
            }

            _position++;
        }
    }
}
