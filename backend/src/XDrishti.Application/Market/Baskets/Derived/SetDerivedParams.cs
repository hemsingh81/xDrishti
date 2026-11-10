using System.Text.Json;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// An owner act (the endpoint needs a step-up): a new parameter set for a derived basket (a set is never edited: new numbers make a new row, validated by the engine's ranges) and/or the basket's switch. Audited with the setting snapshot and the old and new
/// hash. Baskets that this build cannot form (the intraday 4 and 12) cannot be switched on; 13 can, and forms only once the dated lists of BK-05 are recorded.
/// </summary>
public sealed record SetDerivedParamsCommand(Guid BasketId, IReadOnlyDictionary<string, double>? Overrides, bool? Enabled, string Actor) : ICommand;

internal sealed class SetDerivedParamsHandler(IDerivedVersionStore store, IDerivedSettings settings, TimeProvider clock) : ICommandHandler<SetDerivedParamsCommand, DerivedDefinitionView>
{
    public async ValueTask<Result<DerivedDefinitionView>> HandleAsync(SetDerivedParamsCommand command, CancellationToken cancellationToken)
    {
        if (command.Overrides is null && command.Enabled is null)
        {
            return Result.Failure<DerivedDefinitionView>(BasketErrors.Invalid("overrides", "Send overrides (the numbers that differ from the defaults) and/or enabled."));
        }

        var definition = (await store.ListDefinitionsAsync(cancellationToken)).FirstOrDefault(d => d.BasketId == command.BasketId);
        if (definition is null)
        {
            return Result.Failure<DerivedDefinitionView>(BasketErrors.NotFound);
        }

        if (command.Enabled == true && DerivedSchedule.WhyNotRunnable(DerivedCatalogue.Get(definition.DefinitionNo)) is { } why)
        {
            return Result.Failure<DerivedDefinitionView>(BasketErrors.Invalid("enabled", $"This basket cannot be switched on yet ({why})."));
        }

        var before = DerivedComputation.ParamsOf(definition);
        var after = command.Overrides is null ? before : DerivedParams.Create(1, command.Overrides);
        if (after.IsFailure)
        {
            return Result.Failure<DerivedDefinitionView>(BasketErrors.Invalid("overrides", after.Error.Message));
        }

        var now = clock.GetUtcNow();
        var newSet = command.Overrides is not null && before.IsSuccess && before.Value.Hash != after.Value.Hash
            ? new NewParamSet(Guid.CreateVersion7(now), definition.DefinitionNo, JsonSerializer.Serialize(command.Overrides), after.Value.Hash, now)
            : null;
        var enabled = command.Enabled is { } on && on != definition.Enabled ? on : (bool?)null;
        if (newSet is not null || enabled is not null)
        {
            var text = $"Derived basket '{definition.BasketName}': " + (enabled is { } e ? $"switched {(e ? "on" : "off")}; " : string.Empty)
                + (newSet is not null ? $"parameters {(before.IsSuccess ? before.Value.Hash[..12] : "?")} -> {newSet.ParamsHash[..12]}; " : string.Empty)
                + $"settings {settings.Snapshot()}";
            await store.SaveDefinitionAsync(command.BasketId, enabled, newSet, text, command.Actor, cancellationToken);
        }

        var saved = (await store.ListDefinitionsAsync(cancellationToken)).First(d => d.BasketId == command.BasketId);
        return Result.Success(new DerivedDefinitionView(saved.BasketId, saved.BasketName, saved.DefinitionNo, saved.Enabled, DerivedComputation.ParamsOf(saved).Value.Hash, saved.Overrides));
    }
}
