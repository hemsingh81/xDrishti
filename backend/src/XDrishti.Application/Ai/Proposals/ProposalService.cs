using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// The proposal service (<see cref="Id"/>, spec P9-08, ADR 0142): after the weekly digest it lets the local model propose up to three hypotheses or basket changes from an engine-selected pack; the engine checks them and the owner decides. It starts <b>paused</b> and, with
/// <c>Assistant:Proposals:Enabled</c> or the assistant's master switch off, does nothing. It is a service of its own: the nightly run and the weekly digest never wait for it, and its failure is a failed service run only. It writes insert-only inbox rows; it places no order and decides nothing.
/// </summary>
public sealed class ProposalService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored rows: never rename it).</summary>
    public const string Id = "proposals";

    /// <summary>Recorded as the maker of a scheduled proposal.</summary>
    public const string Requester = "system:proposals";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Hypothesis and basket proposals",
        "After the weekly digest, lets the local model propose up to three hypotheses or basket changes from an engine-selected pack of stored statistics. The engine re-reads the evidence and checks every rule; only what passes lands in the suggestions inbox. Nothing is applied until the owner accepts.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Weekly],
        new ServiceSettings(false, Schedule.AfterService(WeeklyService.Id), 0, 240, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            MustFollow = [WeeklyService.Id],
            StartAdvice = "It reads the cell statistics and the report engine; run it after the weekly digest. It needs Assistant:Proposals:Enabled, the assistant's master switch and a ready model; otherwise it does nothing.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GenerateProposalsCommand, ProposalRunView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new GenerateProposalsCommand(null, null, false, manual ? context.RequestedBy : Requester), cancellationToken);
        return Outcome(result);
    }

    internal static ServiceOutcome Outcome(Result<ProposalRunView> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsFailure)
        {
            return result.Error.Code is ProposalErrors.DisabledCode or ProposalErrors.BusyCode ? ServiceOutcome.Skipped(result.Error.Message) : ServiceOutcome.Failed(result.Error.Message);
        }

        var v = result.Value;
        var codes = v.Refused.Count == 0 ? string.Empty : "; refused: " + string.Join(", ", v.Refused.Select(r => r.Code).Distinct(StringComparer.Ordinal));
        return ServiceOutcome.Succeeded($"{v.State}: {v.Stored} stored, {v.Refused.Count} refused, {v.AutoAccepted} auto-accepted{codes}.");
    }
}
