using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.LearnedSettings;

/// <summary>Registers the handlers of learned changes. Only the Api host calls this, because only it provides <see cref="ILearnedConfig"/> (the Confidence settings live there); the other hosts must not resolve them.</summary>
public static class LearnedSettingsRegistration
{
    public static IServiceCollection AddLearnedSettingsHandlers(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetLearnedChangesQuery, LearnedChangesView>, GetLearnedChangesHandler>();
        services.AddScoped<IQueryHandler<GetLearnedChangeQuery, LearnedChangeView?>, GetLearnedChangeHandler>();
        services.AddScoped<LearnedDecisionSupport>();
        services.AddScoped<ICommandHandler<ApproveLearnedChangeCommand, LearnedChangeView>, ApproveLearnedChangeHandler>();
        services.AddScoped<ICommandHandler<DismissLearnedChangeCommand, LearnedChangeView>, DismissLearnedChangeHandler>();
        services.AddScoped<ICommandHandler<UndoLearnedChangeCommand, LearnedChangeView>, UndoLearnedChangeHandler>();
        services.AddScoped<ICommandHandler<RunLearnedSettingsCommand, LearnedRunView>, RunLearnedSettingsHandler>();
        services.TryAddSingleton<ILearnedEvidenceSource, NoLearnedEvidence>(); // no measured source exists yet: unavailable, nothing changes
        services.AddScoped<IEmptyPlanProbe, StoredEmptyPlanProbe>(); // the stored plans: how many sessions in a row the coming plan was empty (criterion 9)
        services.AddScoped<IBellNoticeSource, LearnedTighteningNotices>();
        return services;
    }
}
