using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Ai.Proposals;

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
        services.AddOptions<BriefingOptions>().Bind(configuration.GetSection(BriefingOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DraftOptions>().Bind(configuration.GetSection(DraftOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DraftOptions>>().Value);
        services.AddOptions<ProposalOptions>().Bind(configuration.GetSection(ProposalOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProposalOptions>>().Value);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp =>
        {
            var briefing = sp.GetRequiredService<IOptions<BriefingOptions>>().Value;
            briefing.ReadyWaitSeconds = configuration.GetValue("Assistant:Llm:ReadyWaitSeconds", briefing.ReadyWaitSeconds); // the wait for a ready model is the model client's own setting (P9-01)
            return briefing;
        });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TurnOptions>>().Value);
        services.AddSingleton<IToolCatalog, McpToolCatalog>();
        services.AddSingleton<IChatModel, ChatClientModel>();
        services.AddScoped<IBriefingWriter, BriefingWriter>();
        services.AddScoped<IBriefingStore, BriefingStore>();
        services.AddScoped<IStrategyDrafter, StrategyDrafter>();
        services.AddScoped<IDraftStore, DraftStore>();
        services.AddScoped<IProposalModel, ProposalModel>();
        services.AddScoped<ProposalStore>();
        services.AddScoped<IProposalInbox>(sp => sp.GetRequiredService<ProposalStore>()); // the model's only write: an insert-only inbox row (P9-08)
        services.AddScoped<IProposalReader>(sp => sp.GetRequiredService<ProposalStore>());
        services.AddScoped<IProposalDecisions>(sp => sp.GetRequiredService<ProposalStore>()); // used by the accept, reject and auto handlers only
        services.AddSingleton<IDraftSheetSource, DraftSheetBuilder>();
        services.AddSingleton<IStrategyTextReader, StrategyTextReader>();
        services.AddScoped<IAssistantSettingsStore, AssistantSettingsStore>();
        services.AddScoped<INightlyRunProbe, NightlyRunProbe>();
        services.AddScoped<IModelCallAudit, ModelCallAudit>();
        services.AddScoped<ConversationStore>();
        services.AddScoped<IConversationStore>(sp => sp.GetRequiredService<ConversationStore>());
        services.AddScoped<IConversationReader>(sp => sp.GetRequiredService<ConversationStore>());
        return services;
    }
}
