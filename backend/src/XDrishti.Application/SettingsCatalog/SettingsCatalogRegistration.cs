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
        return services;
    }
}
