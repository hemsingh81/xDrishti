using System.Globalization;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>Checks a kind and a subject and fills the default (spec P9-06 Contract).</summary>
internal static class BriefingSubjects
{
    public static bool KnownKind(string? kind) => kind is BriefingFacts.Daily or BriefingFacts.Weekly;

    public static async ValueTask<Result<string>> ResolveAsync(string? kind, string? subject, IBriefingFactSource source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!KnownKind(kind))
        {
            return Result.Failure<string>(BriefingErrors.Invalid("kind", "The kind is daily or weekly."));
        }

        if (kind == BriefingFacts.Weekly)
        {
            if (subject is null)
            {
                return Result.Success(DigestWeek.Of(IndiaTime.Today(now)).Key);
            }

            return DigestWeek.TryParse(subject) is { } week ? Result.Success(week.Key) : Result.Failure<string>(BriefingErrors.Invalid("subject", "The week is written 2026-W43."));
        }

        if (subject is null)
        {
            return await source.NewestPlanDateAsync(cancellationToken) is { } newest
                ? Result.Success(newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : Result.Failure<string>(BriefingErrors.NotFound("No plan is stored yet."));
        }

        return DateOnly.TryParseExact(subject, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Result.Success(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : Result.Failure<string>(BriefingErrors.Invalid("subject", "The date is written 2026-10-28."));
    }

    /// <summary>Why this subject may not be stored at <paramref name="now"/>, or null (spec P9-06 Scope: a plan date older than 7 days, or a week the digest refuses, is a preview only).</summary>
    public static string? StoreRefusal(string kind, string subject, DateTimeOffset now)
    {
        if (kind == BriefingFacts.Weekly)
        {
            return DigestWeek.TryParse(subject) is { } week ? week.StoreRefusal(now) : "The week is not valid.";
        }

        var today = IndiaTime.Today(now);
        var date = DateOnly.ParseExact(subject, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return date < today.AddDays(-7) ? "A briefing for a plan date older than 7 days can only be previewed." : null;
    }
}
