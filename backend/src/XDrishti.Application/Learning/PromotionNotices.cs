using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The bell notices of automatic promotions (spec CF-10 D7, part 10d-1), derived on read like the other notices (P8-15): so that a promotion by the automatic setting is never silent, one <b>resolved</b> notice for every designation to champion whose actor is <c>auto</c> in the last
/// <see cref="AutoDays"/> days. An owner's designation is not shown (the owner did it). Words and ids only; nothing is stored and nothing is changed by reading it.
/// </summary>
internal sealed class PromotionNotices(ILearningChangelog changelog) : IBellNoticeSource
{
    public const string AutoKind = "promotion_applied_auto";

    /// <summary>How many days back an automatic promotion stays on the bell.</summary>
    public const int AutoDays = 3;

    private const int Limit = 100;

    public async ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await changelog.ListAsync("model", null, Limit, cancellationToken);
        return
        [
            .. rows
                .Where(r => r.Entry is { Kind: "designation", ToState: "champion" } && r.Entry.Actor == MlActors.Auto && r.Entry.At >= now.AddDays(-AutoDays))
                .Select(r => new PlanNotice(
                    "promotion-auto:" + r.Id.ToString(CultureInfo.InvariantCulture),
                    AutoKind,
                    "info",
                    string.Create(CultureInfo.InvariantCulture, $"The automatic setting promoted {r.Entry.Subject.Replace("model:", "model ", StringComparison.Ordinal)} to champion (it met the promotion rule and the influence terms except your switch). Only you re-promote a model after a rollback; a champion does not influence anything until you turn the influence switch on."),
                    "/learning",
                    null,
                    r.Entry.At,
                    r.Entry.At,
                    true)),
        ];
    }
}
