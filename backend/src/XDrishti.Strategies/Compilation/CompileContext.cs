using System.Collections.Frozen;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// What a condition is compiled against: the <paramref name="EvaluationTimeframe"/> of the strategy (an unqualified feature is read there, and <c>time</c> is the close time of a bar of it) and its
/// <paramref name="Parameters"/>, name to number (<c>rsi_len</c> = 21). The compiler only reads the parameters; it never changes the map and keeps no reference to it (it copies it), so the same text compiled with two maps gives two
/// independent plans. The names are matched <b>case-sensitively</b>, like every name in the language, whatever comparer the map has: with a case-insensitive map <c>RSI_LEN</c> is still not the parameter <c>rsi_len</c>.
/// </summary>
public sealed record CompileContext(Timeframe EvaluationTimeframe, IReadOnlyDictionary<string, double> Parameters)
{
    /// <summary>A context for a strategy timeframe and no strategy parameters (the constructor that takes only the evaluation timeframe).</summary>
    public CompileContext(Timeframe evaluationTimeframe)
        : this(evaluationTimeframe, FrozenDictionary<string, double>.Empty)
    {
    }
}
