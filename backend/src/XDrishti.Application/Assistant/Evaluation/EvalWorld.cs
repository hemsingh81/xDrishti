using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The eval files, parsed and hashed (spec P9-09 AC 10): the cases of the three sets, the thresholds and the three hashes of the fingerprint. A file that does not parse refuses the run before anything is asked of the model.</summary>
internal sealed record EvalWorld(
    IReadOnlyList<EvalCase> Data, IReadOnlyList<PlanCase> Plans, IReadOnlyList<IdeaCase> Ideas, ThresholdTable Thresholds, string FixtureHash, string SetsHash, string ThresholdsHash, AnnouncementSetLoad Announcements)
{
    public static Result<EvalWorld> Load(EvalFileSet files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var data = EvalCaseReader.ReadLines(files.DataQuestions);
        if (data.IsFailure)
        {
            return Result.Failure<EvalWorld>(data.Error);
        }

        var plans = EvalSetReader.ReadPlans(files.Plans);
        if (plans.IsFailure)
        {
            return Result.Failure<EvalWorld>(plans.Error);
        }

        var ideas = EvalSetReader.ReadIdeas(files.Ideas);
        if (ideas.IsFailure)
        {
            return Result.Failure<EvalWorld>(ideas.Error);
        }

        var thresholds = ThresholdTable.Parse(files.Thresholds);
        if (thresholds.IsFailure)
        {
            return Result.Failure<EvalWorld>(thresholds.Error);
        }

        var fixture = EvalFixtureReader.Parse(files.Fixture);
        if (fixture.IsFailure)
        {
            return Result.Failure<EvalWorld>(fixture.Error);
        }

        return Result.Success(new EvalWorld(
            data.Value, plans.Value, ideas.Value, thresholds.Value, EvalFixture.HashOf(files.Fixture),
            Sha(files.DataQuestions + "\n--\n" + files.Plans + "\n--\n" + files.Ideas + SetD(files)), Sha(files.Thresholds), AnnouncementSetLoad.Of(files.Announcements, files.AnnouncementBaseline)));
    }

    // Set D enters the sets hash only while its labelled file exists: the manifest, the label file's hash and the baseline file (so a baseline changed after the labels changes the fingerprint). With no file the hash is what it was before P15-07.
    private static string SetD(EvalFileSet files) =>
        files.Announcements is null ? string.Empty : "\n--\n" + files.AnnouncementManifest + "\n--\n" + Sha(files.Announcements) + "\n--\n" + files.AnnouncementBaseline;

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));
}
