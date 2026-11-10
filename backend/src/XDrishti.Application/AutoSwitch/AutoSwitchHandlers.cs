using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.AutoSwitch;

internal sealed class GetAutoHandler(AutoSwitchService service) : IQueryHandler<GetAutoQuery, AutoView>
{
    public ValueTask<AutoView> HandleAsync(GetAutoQuery query, CancellationToken cancellationToken) => service.ReadAsync(cancellationToken);
}

internal sealed class TurnAutoOnHandler(AutoSwitchService service) : ICommandHandler<TurnAutoOnCommand, AutoView>
{
    public ValueTask<Result<AutoView>> HandleAsync(TurnAutoOnCommand command, CancellationToken cancellationToken) => service.TurnOnAsync(command, cancellationToken);
}

internal sealed class TurnAutoOffHandler(AutoSwitchService service) : ICommandHandler<TurnAutoOffCommand, AutoView>
{
    public ValueTask<Result<AutoView>> HandleAsync(TurnAutoOffCommand command, CancellationToken cancellationToken) => service.TurnOffAsync(command, cancellationToken);
}

/// <summary>The plan builder of the nightly: the selection builder while Auto is ON, else the skeleton (spec AU-17). The switch is read at build time, so turning Auto ON or OFF takes effect at the next run.</summary>
internal sealed class SwitchedPlanBuilder(XDrishti.Application.Abstractions.LearnedSettings.IAutoSwitch auto, Planning.SelectionPlanBuilder selection, Planning.NullPlanBuilder skeleton) : Abstractions.Planning.IPlanBuilder
{
    /// <summary>The skeleton's id: the builder is chosen at build time (the switch is read then), so the plan key uses the id of the builder that actually built, not this one; nothing reads this before building.</summary>
    public string Id => skeleton.Id;

    public async ValueTask<Result<Abstractions.Planning.NewPlan>> BuildAsync(Abstractions.Planning.PlanBuildRequest request, CancellationToken cancellationToken) =>
        await auto.IsOnAsync(cancellationToken) ? await selection.BuildAsync(request, cancellationToken) : await skeleton.BuildAsync(request, cancellationToken);
}
