namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>The scripted "perfect" answers of the 30 data questions and the tool result each would have read (spec P9-09 AC 2). Hand-written, so that a change of the grader is checked against what a good assistant would say.</summary>
internal static class PerfectAnswers
{
    public static readonly IReadOnlyDictionary<string, (string Answer, string ToolResult)> ById = new Dictionary<string, (string, string)>
    {
        ["DQ-01"] = ("ORB 15m has 24 trades.", """{"n":24}"""),
        ["DQ-02"] = ("The win rate of ORB 15m is 45.83%.", """{"winRate":{"state":"value","number":0.4583333333333333}}"""),
        ["DQ-03"] = ("The total R of ORB 15m is 4.0 R.", """{"totalR":{"state":"value","number":4.0}}"""),
        ["DQ-04"] = ("The expectancy of ORB 15m is 0.1667 R per trade.", """{"expectancyR":{"number":0.16666666666666666}}"""),
        ["DQ-05"] = ("The profit factor of ORB 15m is 1.3077.", """{"profitFactor":{"number":1.3076923076923077}}"""),
        ["DQ-06"] = ("The average winning trade in ORB 15m is 1.5455 R.", """{"avgWinR":{"number":1.5454545454545454}}"""),
        ["DQ-07"] = ("The largest single win in ORB 15m is 3.0 R, made by EVC.", """{"maxR":3.0,"symbol":"EVC"}"""),
        ["DQ-08"] = ("The win rate of VWAP pullback 15m is 43.75%.", """{"winRate":{"number":0.4375}}"""),
        ["DQ-09"] = ("The total R of EvalAlpha across both runs is 5.5.", """{"totalR":5.5}"""),
        ["DQ-10"] = ("The total R of EvalBeta across both runs is -0.5.", """{"totalR":-0.5}"""),
        ["DQ-11"] = ("EvalAlpha has the higher expectancy (0.275 R against -0.025 R for EvalBeta).", """[{"key":"EvalAlpha","expectancyR":0.275},{"key":"EvalBeta","expectancyR":-0.025}]"""),
        ["DQ-12"] = ("The total R of the SELL trades in ORB 15m is 2.5.", """{"totalR":2.5}"""),
        ["DQ-13"] = ("EvalAlpha BUY and EvalBeta SELL tied at 3.0 R for the highest total R.", """[{"key":"EvalAlpha|BUY","totalR":3.0},{"key":"EvalBeta|SELL","totalR":3.0}]"""),
        ["DQ-14"] = ("There are 8 EvalBeta trades in VWAP pullback 15m.", """{"n":8}"""),
        ["DQ-15"] = ("The unrealised return of EVH1 is 21.0%.", """{"symbol":"EVH1","unrealisedPct":21.0}"""),
        ["DQ-16"] = ("The amount invested in EVH1 is ₹10,000.", """{"invested":10000}"""),
        ["DQ-17"] = ("The unrealised P&L of EVH2 is -₹1,000.", """{"unrealised":-1000}"""),
        ["DQ-18"] = ("The current value of MyLongTerm is ₹21,100.", """{"value":21100}"""),
        ["DQ-19"] = ("MyLongTerm has 2 holdings.", """{"holdings":2}"""),
        ["DQ-20"] = ("The plan for 2026-03-03 has 3 primary tickets.", """{"primary":[{"rank":1},{"rank":2},{"rank":3}]}"""),
        ["DQ-21"] = ("The rank 2 primary ticket is EVD on the SELL side.", """{"rank":2,"symbol":"EVD","side":"SELL"}"""),
        ["DQ-22"] = ("EvalPaper gets 200 shares of EVA in the plan for 2026-03-03.", """{"account":"EvalPaper","symbol":"EVA","shares":200}"""),
        ["DQ-23"] = ("The statistics cell ORB 15m BUY intraday holds 12 trades.", """{"n":12}"""),
        ["DQ-24"] = ("2 lessons are open.", """{"open":2}"""),
        ["DQ-25"] = ("2 hypotheses are open.", """{"open":2}"""),
        ["DQ-26"] = ("ORB has more stored trades (24) than VWAP pullback (16).", """[{"strategy":"ORB","n":24},{"strategy":"VWAP_PULLBACK","n":16}]"""),
        ["DQ-27"] = ("The market regime of the 2026-03-03 plan is not available: market context is not stored.", """{"state":"unavailable"}"""),
        ["DQ-28"] = ("The net expectancy of ORB 15m is not available: there is no confirmed charge sheet, so the figures are gross only.", """{"state":"unavailable","reason":"no confirmed charge sheet"}"""),
        ["DQ-29"] = ("The win rate of EVA SELL trades in ORB 15m is insufficient: only 2 trades.", """{"n":2,"winRate":{"state":"insufficient"}}"""),
        ["DQ-30"] = ("The champion model has no calibration data: none stored.", """{"state":"empty"}"""),
    };
}
