using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The report engine for the tool server (spec P9-05, ADR 0139): the handlers the Reports screen uses, the gate that lets one report at a time hold a population in memory, and the reader that reshapes their views. The host supplies the ports (trade source, charge-sheet confirmation, classifications).
/// Called only by the read-only registration of the infrastructure, so the tool server reaches the application through <see cref="IAiReportReader"/> alone.
/// </summary>
public static class AssistantToolsRegistration
{
    public static IServiceCollection AddAssistantReportTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ReportGate>();
        services.AddScoped<ReportLoader>();
        services.AddScoped<IQueryHandler<ListReportRunsQuery, Result<RunsView>>, ListReportRunsHandler>();
        services.AddScoped<IQueryHandler<GetReportSummaryQuery, Result<SummaryView>>, GetReportSummaryHandler>();
        services.AddScoped<IQueryHandler<GetReportGroupsQuery, Result<GroupsView>>, GetReportGroupsHandler>();
        services.AddScoped<IQueryHandler<GetReportPivotQuery, Result<PivotView>>, GetReportPivotHandler>();
        services.AddScoped<IQueryHandler<GetReportEquityQuery, Result<EquityView>>, GetReportEquityHandler>();
        services.AddScoped<IQueryHandler<GetReportDistributionQuery, Result<DistributionView>>, GetReportDistributionHandler>();
        services.AddScoped<IQueryHandler<GetReportInsightsQuery, Result<InsightsView>>, GetReportInsightsHandler>();
        services.AddScoped<IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>>, GetReportWhatWorksHandler>();
        services.AddSingleton<IAiReportReader, ReportToolReader>();
        return services;
    }
}
