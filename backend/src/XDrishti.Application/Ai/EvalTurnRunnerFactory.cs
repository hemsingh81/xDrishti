using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai.Drafting;

namespace XDrishti.Application.Ai;

/// <summary>
/// Builds the one turn runner of the eval (spec P9-09 AC 8, 13, ADR 0143): the assistant's own turn service over the <b>eval</b> tool catalog (the read-only server on the fixture database) and options fixed for the run: temperature 0, seed 1337, no history, one question at a time
/// (the shared turn slot). It is the only code that hands the turn service a catalog other than the production one, and it hands it the eval catalog only. The production runner is not touched.
/// </summary>
internal static class EvalTurnRunnerFactory
{
    public const long Seed = 1337;

    public static IAssistantTurnRunner Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var basis = services.GetRequiredService<TurnOptions>();
        var options = new TurnOptions
        {
            MaxMessageChars = basis.MaxMessageChars,
            HistoryMessages = 0,
            MaxAnswerTokens = basis.MaxAnswerTokens,
            TurnTimeoutSeconds = basis.TurnTimeoutSeconds,
            Temperature = 0,
            Seed = Seed,
            ToolResultTextBudget = basis.ToolResultTextBudget,
            ToolResultCutChars = basis.ToolResultCutChars,
            PingSeconds = basis.PingSeconds,
            NightlyGuardMinutes = basis.NightlyGuardMinutes,
            PromptBudgetTokens = basis.PromptBudgetTokens,
        };
        return new AssistantTurnService(
            services.GetRequiredService<IAssistantSettingsStore>(),
            services.GetRequiredService<ILlmHealth>(),
            services.GetRequiredService<IEvalToolCatalog>(),
            services.GetRequiredService<INightlyRunProbe>(),
            services.GetRequiredService<IConversationStore>(),
            services.GetRequiredService<IChatModel>(),
            services.GetRequiredService<TurnGate>(),
            options,
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<DraftProposalTool>(),
            services.GetRequiredService<ILogger<AssistantTurnService>>());
    }
}
