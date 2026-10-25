using XDrishti.Strategies.Mirror;
using static XDrishti.Strategies.Tests.Mirror.MirrorTestSupport;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>Spec P3-02e criterion 6: each refusal gives its code, a message naming the offending text and the advice to write a sell block; every one has a near miss that is accepted.</summary>
public sealed class ConditionMirrorRefusalTests
{
    public static readonly TheoryData<string, string, string> Failing = new()
    {
        { "close > 100", MirrorCodes.AbsoluteLevel, "close > 100" },
        { "rsi(14) > close", MirrorCodes.MixedOrientation, "rsi(14) > close" },
        { "close > ema(20) * 1.01", MirrorCodes.NotMirrorable, "ema(20) * 1.01" },
        { "regime() == 1", MirrorCodes.ContextName, "regime()" },
        { "vix > 20", MirrorCodes.ContextName, "vix" },
        { "day_of_week == MON", MirrorCodes.ContextName, "day_of_week" },
        { "is_expiry_day == 1", MirrorCodes.ContextName, "is_expiry_day" },
        { "index.trend == 1", MirrorCodes.ContextName, "index.trend" },
        { "breadth > 60", MirrorCodes.ContextName, "breadth" },
        { "abs(close) > 1", MirrorCodes.NotMirrorable, "abs(close)" },
        { "close + open > 5", MirrorCodes.NotMirrorable, "close + open" },
        { "rsi(14) > 55.1234567890123456", MirrorCodes.LiteralPrecision, "rsi(14) > 55.12345" },
        { "atr(14) / close > 0.01", MirrorCodes.NotMirrorable, "atr(14) / close" },
        { "-close < 5", MirrorCodes.NotMirrorable, "-close" },
        { "min(rsi(14), 50) > 40", MirrorCodes.NotMirrorable, "min(rsi(14), 50)" },
        { "5 - atr(14) > 0 and 5 - close > 0", MirrorCodes.NotMirrorable, "5 - close" },
        { "macd_hist > atr(14)", MirrorCodes.MixedOrientation, "macd_hist > atr(14)" },
    };

    public static readonly TheoryData<string> NearMisses =
    [
        "close > open",
        "rsi(14) > rsi(2)",
        "close > ema(20) + 5",
        "adx(14) > 20 and volume > 1000",
        "time > \"09:30\" and rel_volume(20) > 1",
        "volume > 0 and plus_di(14) > minus_di(14)",
        "abs(close - open) > 1",
        "close - open > 5",
        "rsi(14) > 55.5",
        "atr(14) / 2 > 0.01",
        "-macd_hist < 5",
        "min(close, open) > ema(20)",
        "5 - atr(14) > 0",
    ];

    [Theory]
    [MemberData(nameof(Failing))]
    public void A_condition_without_a_single_mirror_is_refused_with_its_code_and_the_offending_text(string source, string code, string fragment)
    {
        var result = Try(source);

        var problem = result.Problem.ShouldNotBeNull(source);
        problem.Code.ShouldBe(code);
        problem.Message.ShouldContain(fragment);
        problem.Message.ShouldContain("sell:");
        problem.Suggestion.ShouldNotBeNull().ShouldContain("sell:");
        problem.Severity.ShouldBe(ProblemSeverity.Error);
        result.IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void The_nearest_accepted_text_mirrors(string source)
    {
        Try(source).IsSuccess.ShouldBeTrue(source + " -> " + Try(source).Problem?.Message);
    }
}
