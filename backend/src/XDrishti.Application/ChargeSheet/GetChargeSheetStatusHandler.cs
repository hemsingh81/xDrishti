using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.ChargeSheet;

/// <summary>Reads the status of the charge-sheet confirmation (spec P8-23). Reads only: it depends on the read port of the log and on no write port.</summary>
internal sealed class GetChargeSheetStatusHandler(ChargeSheetStatusBuilder builder) : IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>>
{
    public async ValueTask<Result<ChargeSheetStatusView>> HandleAsync(GetChargeSheetStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.SheetId is { } id && !CostSheet.IsValidId(id))
        {
            return Result.Failure<ChargeSheetStatusView>(Error.Validation("chargesheet.sheet_invalid", "A sheet id is lower case letters, digits, '.', '_' or '-'."));
        }

        return Result.Success(await builder.BuildAsync(query.SheetId, cancellationToken));
    }
}
