using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SettingsCatalog;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The owner's choice of the index of the basket profile and ranking, <c>reports.basket_benchmark_instrument</c> (BK-06c): any listed index of the master. It loosens nothing, so a session is enough (no password). An unknown id, a non-index or a retired index is
/// <c>reports.invalid</c>; choosing the index already in force records nothing. A change is audited with the key and the symbols (no figures).
/// </summary>
public sealed record SetBasketBenchmarkCommand(Guid? InstrumentId, string Actor) : ICommand;

internal sealed class SetBasketBenchmarkHandler(
    IPortfolioBenchmark master,
    IBasketBenchmark current,
    IEssentialSettingStore store,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SetBasketBenchmarkHandler> logger) : ICommandHandler<SetBasketBenchmarkCommand, ProfileIndexView>
{
    public async ValueTask<Result<ProfileIndexView>> HandleAsync(SetBasketBenchmarkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<ProfileIndexView>(Error.Forbidden("settings.owner_only", "Only the signed-in owner changes a setting."));
        }

        if (command.InstrumentId is not { } id || id == Guid.Empty)
        {
            return Result.Failure<ProfileIndexView>(ReportErrors.Invalid("instrumentId is required: the id of an index of the instrument master."));
        }

        var instrument = await master.FindAsync(id, cancellationToken);
        if (instrument is null || instrument.Type != InstrumentTypeCodes.Index || instrument.Retired)
        {
            return Result.Failure<ProfileIndexView>(ReportErrors.Invalid("instrumentId is not a listed index of the instrument master."));
        }

        var before = await current.GetAsync(cancellationToken);
        if (before is { IsDefault: false, Index: { } same } && same.Id == id)
        {
            return Result.Success(Of(same));
        }

        var now = clock.GetUtcNow();
        await store.SetAsync(AppSettingKeys.BasketBenchmarkInstrument, id.ToString("D"), command.Actor, now, cancellationToken);
        await EssentialAudit.RecordAsync(events, unitOfWork, AccountEventType.EssentialSettingChanged,
            string.Create(CultureInfo.InvariantCulture, $"{AppSettingKeys.BasketBenchmarkInstrument}={instrument.Symbol} (was {before.Index?.Symbol ?? before.Name})"), command.Actor, now, logger);
        return Result.Success(Of(instrument));
    }

    private static ProfileIndexView Of(BenchmarkInstrument i) => new(i.Id, i.Symbol, false, i.Name);
}
