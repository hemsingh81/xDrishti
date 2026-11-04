using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Reads the ledger of a family with its variant count and the tightening factor in force (spec P5-03).</summary>
internal sealed class GetLedgerHandler(IStrategyStore strategies, ITestLedger ledger, LabSettings settings, RegistrationOptions? registration = null) : IQueryHandler<GetLedgerQuery, Result<LedgerView>>
{
    public async ValueTask<Result<LedgerView>> HandleAsync(GetLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await strategies.GetAsync(query.StrategyId, cancellationToken) is null)
        {
            return Result.Failure<LedgerView>(LabErrors.NotFound(query.StrategyId));
        }

        var variants = await ledger.CountVariantsAsync(query.StrategyId, cancellationToken);
        var rows = await ledger.ListAsync(query.StrategyId, cancellationToken);
        return Result.Success(new LedgerView(query.StrategyId, variants, MultipleTesting.Factor(MultipleTesting.Effective(variants, settings.MultipleTesting)), [.. rows.Select(LedgerView.Of)], await TrialsAsync(query.StrategyId, cancellationToken)));
    }

    private async ValueTask<TrialsView?> TrialsAsync(string strategyId, CancellationToken cancellationToken)
    {
        var parts = await ledger.CountTrialsAsync(strategyId, cancellationToken);
        return parts is null ? null : TrialsView.Of(parts, registration is { CountInFactor: true }, settings.MultipleTesting);
    }
}
