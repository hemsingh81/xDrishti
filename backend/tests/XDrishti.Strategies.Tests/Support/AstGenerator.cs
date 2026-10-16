using System.Collections.Immutable;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// Random syntax trees for the round-trip tests: every node kind, every operator, calls with named arguments, qualified references, subscripts, numbers of every size (including random bit patterns and
/// subnormals), strings with spaces and Unicode, and names that begin like keywords (<c>order</c>, <c>android</c>, <c>nothing</c>, <c>true_</c>). The same seed gives the same tree. A tree is always one the printer can write:
/// a subscript only on a reference, no negative numbers, no string with a quote or a line break, no call named <c>tf</c>.
/// </summary>
public sealed class AstGenerator(int seed)
{
    private static readonly string[] Names =
    [
        "close", "rsi", "ema", "vwap", "prev_day_high", "x", "y1", "_z", "time", "MON", "order", "android", "nothing", "true_", "tf_x", "not_a", "and1", "orx", "f", "regime", "tf", "or_high", "T",
    ];

    private static readonly string[] Scopes = ["prev_day", "today", "index", "a", "tf_"];

    private static readonly string[] Frames = ["1m", "5m", "1h", "1D", "1W", "1M", string.Empty, "x y", "भारत"];

    private static readonly string[] Texts = [string.Empty, "09:45", "UP_LOW_VOL", "a b", "it's", "back\\slash", "tab\there", "भारत", "😀", "＞", "tf(\"x\")".Replace("\"", "'")];

    private static readonly string[] SourceNames = ["close", "open", "high", "low"];

    private readonly Random _random = new(seed);

    /// <summary>A random tree whose depth is at most <paramref name="depth"/> levels of operators and calls.</summary>
    public Expression Expression(int depth)
    {
        if (depth <= 0 || _random.Next(100) < 20)
        {
            return Leaf();
        }

        return _random.Next(10) switch
        {
            0 => new Unary(UnaryOperator.Not, Expression(depth - 1), default),
            1 => new Unary(UnaryOperator.Negate, Expression(depth - 1), default),
            2 or 3 => Call(depth),
            _ => new Binary(Pick(Enum.GetValues<BinaryOperator>()), Expression(depth - 1), Expression(depth - 1), default),
        };
    }

    private Expression Leaf() => _random.Next(10) switch
    {
        0 or 1 or 2 => new NumberLiteral(Number(), default),
        3 => new StringLiteral(Pick(Texts), default),
        4 => new BoolLiteral(_random.Next(2) == 0, default),
        5 or 6 => Reference(),
        7 => new Subscript(Reference(), _random.Next(2) == 0 ? _random.Next(0, 300) : -_random.Next(1, 5), default, default),
        _ => Reference(),
    };

    private Reference Reference() => _random.Next(4) switch
    {
        0 => new Reference(Id(Pick(Names)), default, Id(Pick(Scopes))),
        1 => new Reference(Id(Pick(Names)), default, null, new StringLiteral(Pick(Frames), default)),
        _ => new Reference(Id(Pick(Names)), default),
    };

    private Call Call(int depth)
    {
        var name = Id(Pick(Names.Where(n => n != "tf").ToArray()));
        var count = _random.Next(0, 4);
        var arguments = ImmutableArray.CreateBuilder<Argument>();
        for (var i = 0; i < count; i++)
        {
            arguments.Add(_random.Next(5) == 0
                ? new NamedArgument(Id(Pick(Names.Where(n => n != "tf").ToArray())), Id(Pick(SourceNames)), default)
                : new PositionalArgument(Expression(depth - 1), default));
        }

        var frame = _random.Next(4) == 0 ? new StringLiteral(Pick(Frames), default) : null;
        return new Call(name, arguments.ToImmutable(), default, frame);
    }

    private double Number()
    {
        switch (_random.Next(6))
        {
            case 0:
                return _random.Next(0, 1000);
            case 1:
                return Math.Round(_random.NextDouble() * 1000, _random.Next(0, 6));
            case 2:
                return _random.NextDouble() * Math.Pow(10, _random.Next(-12, 25));
            case 3:
                return Pick<double>([0, 1, 0.5, 0.001, 55, 1e-7, 1e21, 1e300, double.Epsilon, double.MaxValue, 123456789012345680000.0]);
            default:
                // A random bit pattern: any finite non-negative double, subnormals and huge values included.
                while (true)
                {
                    var value = BitConverter.Int64BitsToDouble(_random.NextInt64() & 0x7FFFFFFFFFFFFFFFL);
                    if (double.IsFinite(value))
                    {
                        return value;
                    }
                }
        }
    }

    private static Identifier Id(string text) => new(text, default);

    private T Pick<T>(T[] items) => items[_random.Next(items.Length)];
}
