using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Lab;

/// <summary>Validates a strategy text without writing anything (spec P5-01, criteria 1, 2 and 9). A text over the limit is refused before it is read, as a problem with a code like any other.</summary>
internal sealed class ValidateStrategyHandler(IStrategyChecker checker, ILogger<ValidateStrategyHandler> logger) : IQueryHandler<ValidateStrategyQuery, StrategyValidation>
{
    public ValueTask<StrategyValidation> HandleAsync(ValidateStrategyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(query.Text) > LabErrors.MaxTextBytes)
        {
            return ValueTask.FromResult(new StrategyValidation(false, null, null, null, null, [], [], [LabErrors.TooLarge()]));
        }

        var check = checker.Check(query.Text);
        var d = check.Description;
        var result = new StrategyValidation(
            d is not null,
            d?.Hash,
            d?.Id,
            d?.Style,
            check.Version,
            [.. d?.Timeframes.Select(t => t.ToCode()) ?? []],
            [.. d?.Sides.Select(s => s.ToString().ToUpperInvariant()) ?? []],
            check.Issues);
        if (d is null)
        {
            var errors = check.Issues.Count(i => i.Severity == "error");
            var warnings = check.Issues.Count(i => i.Severity == "warning");
            LabLog.Rejected(logger, errors, warnings);
        }

        return ValueTask.FromResult(result);
    }
}
