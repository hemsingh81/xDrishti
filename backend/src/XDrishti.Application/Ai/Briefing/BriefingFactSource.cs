using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>
/// Reads what a briefing is built from (spec P9-06 Needs): the newest plan of a date with its tickets, the digest of a week and the report insights (the built P8-06 handler). Read ports only; the facts come out of <see cref="BriefingFactsBuilder"/>.
/// </summary>
internal sealed class BriefingFactSource(
    IPlanStore plans,
    IDigestStore digests,
    IQueryHandler<GetReportInsightsQuery, Result<InsightsView>> insights,
    BriefingOptions options) : IBriefingFactSource
{
    public async ValueTask<DateOnly?> NewestPlanDateAsync(CancellationToken cancellationToken)
    {
        var newest = await plans.ListAsync(null, null, 1, cancellationToken);
        return newest.Count == 0 ? null : newest[0].Plan.ForDate;
    }

    public async ValueTask<Result<BriefingInput>> BuildAsync(string kind, string subject, bool portfolioContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(subject);
        var builder = new BriefingFactsBuilder();
        if (kind == Domain.Assistant.Text.BriefingFacts.Daily)
        {
            var date = DateOnly.ParseExact(subject, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var found = await plans.ListAsync(date, null, 1, cancellationToken);
            if (found.Count == 0)
            {
                return Result.Failure<BriefingInput>(BriefingErrors.NotFound($"No plan is stored for {subject}."));
            }

            var rows = await plans.ItemsAsync(found[0].Id, cancellationToken);
            var plan = Planning.PlanView.Of(found[0], [], []);
            var facts = builder.Daily(plan, [.. rows.Select(Planning.PlanItemView.Of)], portfolioContext);
            return Result.Success(new BriefingInput(facts, builder.InjectionFlags));
        }

        var stored = await digests.FindByWeekAsync(subject, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<BriefingInput>(Error.Conflict(BriefingErrors.NotEvaluableCode, $"not_evaluable: no digest stored for {subject}."));
        }

        var report = await insights.HandleAsync(new GetReportInsightsQuery(ReportQuery.Default), cancellationToken);
        var weekly = builder.Weekly(subject, stored.Id, stored.Digest.Sections, report.IsSuccess ? report.Value : null, options.MaxPromptTokens * 3);
        return Result.Success(new BriefingInput(weekly, builder.InjectionFlags));
    }
}
