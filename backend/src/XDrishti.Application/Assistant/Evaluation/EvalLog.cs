using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>Log events 6190 to 6195 (spec P9-09 AC 14). Ids, counts and milliseconds only: never an answer, a question or a tool result.</summary>
internal static partial class EvalLog
{
    [LoggerMessage(EventId = 6190, Level = LogLevel.Information, Message = "assistant.eval.started run={Run} sets={Sets} repeats={Repeats} cases={Cases}")]
    public static partial void Started(ILogger logger, Guid run, string sets, int repeats, int cases);

    [LoggerMessage(EventId = 6191, Level = LogLevel.Information, Message = "assistant.eval.case set={Set} case={Case} repeat={Repeat} passed={Passed} ms={Ms}")]
    public static partial void CaseDone(ILogger logger, string set, string @case, int repeat, bool passed, int ms);

    [LoggerMessage(EventId = 6192, Level = LogLevel.Information, Message = "assistant.eval.finished run={Run} status={Status} verdict={Verdict} gating={Gating} ms={Ms}")]
    public static partial void Finished(ILogger logger, Guid run, string status, string verdict, bool gating, long ms);

    [LoggerMessage(EventId = 6193, Level = LogLevel.Information, Message = "assistant.eval.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 6194, Level = LogLevel.Information, Message = "assistant.eval.accepted run={Run} created={Created}")]
    public static partial void Accepted(ILogger logger, Guid run, bool created);

    [LoggerMessage(EventId = 6195, Level = LogLevel.Information, Message = "assistant.eval.graded run={Run} answers={Answers} passed={Passed}")]
    public static partial void Graded(ILogger logger, Guid run, int answers, int passed);
}
