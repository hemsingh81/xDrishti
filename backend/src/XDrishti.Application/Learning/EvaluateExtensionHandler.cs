using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// The verb <c>xd learn extension-eval</c> (P15-10): one family at a time, each by its pre-registered protocol. The handler only routes; the protocols are the domain functions <see cref="ProtocolF"/>, <see cref="ProtocolX"/>, <see cref="ProtocolP"/> and <see cref="ProtocolL"/>, and every trial is
/// counted in the ledger before its numbers. Research only: a stored KEEP is a record; its only reader is the event-filter verdict port, behind the owner's default-OFF auto option.
/// </summary>
internal sealed class EvaluateExtensionHandler(FilterEvaluation filter, ComparisonEvaluation comparison, PeadEvaluation pead, FlagEvaluation flag) : ICommandHandler<EvaluateExtensionCommand, ExtensionEvaluationOutcome>
{
    public async ValueTask<Result<ExtensionEvaluationOutcome>> HandleAsync(EvaluateExtensionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Family switch
        {
            ExtensionFamilies.Filter => await filter.RunAsync(command.HoldOut, cancellationToken),
            ExtensionFamilies.EventFeatures or ExtensionFamilies.FundamentalFeatures => await comparison.RunAsync(command.Family, command.HoldOut, cancellationToken),
            ExtensionFamilies.Pead => await pead.RunAsync(command.HoldOut, cancellationToken),
            ExtensionFamilies.Flag => await flag.RunAsync(cancellationToken),
            _ when OptionFeatureFamilies.IsKnown(command.Family) => await comparison.RunAsync(command.Family, command.HoldOut, cancellationToken),
            _ => Result.Failure<ExtensionEvaluationOutcome>(ExtensionEvaluationErrors.InvalidFamily(command.Family)),
        };
    }
}
