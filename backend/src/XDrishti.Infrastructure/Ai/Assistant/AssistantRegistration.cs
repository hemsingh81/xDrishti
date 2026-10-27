using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// Registers the assistant's infrastructure (spec P9-03, ADR 0138): the settings store, the audit stores, the nightly probe, the chat model adapter and the tool-server client. Nothing is called at start-up; with the assistant off (the default) nothing is called at all.
/// </summary>
public static class AssistantRegistration
{
    public static IServiceCollection AddAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<TurnOptions>().Bind(configuration.GetSection(TurnOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AssistantMcpOptions>()
            .Bind(configuration.GetSection(AssistantMcpOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.HasValidUrl(), "Assistant:Mcp:Url must be an absolute http or https address.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TurnOptions>>().Value);
        services.AddSingleton<IToolCatalog, McpToolCatalog>();
        services.AddSingleton<IChatModel, ChatClientModel>();
        services.AddScoped<IAssistantSettingsStore, AssistantSettingsStore>();
        services.AddScoped<INightlyRunProbe, NightlyRunProbe>();
        services.AddScoped<IModelCallAudit, ModelCallAudit>();
        services.AddScoped<ConversationStore>();
        services.AddScoped<IConversationStore>(sp => sp.GetRequiredService<ConversationStore>());
        services.AddScoped<IConversationReader>(sp => sp.GetRequiredService<ConversationStore>());
        return services;
    }
}
