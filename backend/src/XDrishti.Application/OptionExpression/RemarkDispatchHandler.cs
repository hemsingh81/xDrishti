using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options remark</c> (P14-08, P14-10): one verb, the kind of the registered profile picks the use case. An <c>option_buy</c> profile is re-marked by <see cref="RemarkRunHandler"/>, a <c>debit_spread</c> or <c>credit_spread</c> profile by <see cref="RemarkStructureHandler"/>;
/// an <c>iron_condor</c> profile has no cash run to re-mark and is simulated by <c>xd options condors</c>.
/// </summary>
internal sealed class RemarkDispatchHandler(IExpressionProfileStore profiles, RemarkRunHandler single, RemarkStructureHandler structures) : ICommandHandler<RemarkRunCommand, RemarkRunSummary>
{
    public const string CondorHasNoCashRunCode = "expression.condor_has_no_cash_run";

    public async ValueTask<Result<RemarkRunSummary>> HandleAsync(RemarkRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var stored = await profiles.GetAsync(command.ProfileHash, cancellationToken);
        return stored?.Profile.Kind switch
        {
            Expression.DebitSpread or Expression.CreditSpread => await structures.HandleAsync(command, stored, cancellationToken),
            Expression.IronCondor => Result.Failure<RemarkRunSummary>(Error.Validation(CondorHasNoCashRunCode, "An iron_condor profile has no cash run to re-mark: simulate it with 'xd options condors'.")),
            _ => await single.HandleAsync(command, cancellationToken),
        };
    }
}
