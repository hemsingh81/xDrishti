using System.Text.Json;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The stored JSON of the eval: the summary of a run (read back by accept, compare and status) and the detail of a case. One place, so that the row and the view agree.</summary>
internal static class EvalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(RunSummary summary) => JsonSerializer.Serialize(summary, Options);

    public static RunSummary? ReadSummary(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RunSummary>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Detail(AnswerGrade grade, IReadOnlyList<string> toolsExpected) =>
        JsonSerializer.Serialize(new { grade.Correct, grade.Grounded, grade.HardFail, codes = grade.Codes, ungrounded = grade.UngroundedFigures, toolsExpected }, Options);

    public static string Detail(PlanGrade grade) =>
        JsonSerializer.Serialize(new { raw = grade.RawPass, repaired = grade.RepairedPass, escape = grade.Escape, covered = grade.Covered, findings = grade.Findings, absent = grade.Absent }, Options);

    public static string Detail(IdeaGrade grade, bool firstShot) =>
        JsonSerializer.Serialize(new { accepted = grade.Accepted, firstShot, passed = grade.PassedAssertions, total = grade.TotalAssertions, intent = grade.Intent, declineCorrect = grade.DeclineCorrect, failed = grade.Failed }, Options);
}
