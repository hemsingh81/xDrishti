using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.SettingsCatalog;

/// <summary>Registers the catalog handler. Only the Api host calls this, because only it provides the <c>ISettingsCatalogSource</c> the handler needs; the Feed, Worker and CLI hosts must not resolve it.</summary>
public static class SettingsCatalogRegistration
{
    public static IServiceCollection AddSettingsCatalogHandler(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetSettingsCatalogQuery, SettingsCatalogResponse>, GetSettingsCatalogHandler>();
        services.AddScoped<IQueryHandler<GetEssentialSettingsQuery, EssentialSettingsView>, GetEssentialSettingsHandler>();
        services.AddScoped<ICommandHandler<ChangeEssentialSettingCommand, EssentialSettingView>, ChangeEssentialSettingHandler>();
        services.AddScoped<ICommandHandler<ResetEssentialSettingCommand, EssentialSettingView>, ResetEssentialSettingHandler>();
        services.AddScoped<IQueryHandler<XDrishti.Application.Auto.EvaluateAutoProgrammeCheckQuery, XDrishti.Domain.Common.Result<XDrishti.Application.Auto.AutoProgrammeCheckView>>, XDrishti.Application.Auto.EvaluateAutoProgrammeCheckHandler>(); // the AU programme check reads the catalog, so it lives with its source in the Api only (AU-19, ADR 0253)
        return services;
    }
}
