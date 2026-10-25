using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Mirror;

internal static class MirrorTestSupport
{
    public static readonly IReadOnlySet<string> Parameters = new HashSet<string>(StringComparer.Ordinal)
    {
        "gap_min", "gap_max", "pull_atr", "rsi_hi", "adx_max", "level", "fast_len", "slow_len",
    };

    public static ConditionMirrorResult Try(string source) => ConditionMirror.Mirror(source, Parameters);

    public static string Ok(string source)
    {
        var result = Try(source);
        result.IsSuccess.ShouldBeTrue(source + " -> " + result.Problem?.Message);
        return result.Text!;
    }

    public static Expression Tree(string source) => ConditionParser.Parse(source).Tree.ShouldNotBeNull(source);
}
