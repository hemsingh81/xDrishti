using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionExpression;

namespace XDrishti.Infrastructure.OptionExpression;

internal sealed class RemarkSettings(IOptions<RemarkOptions> options) : IRemarkSettings
{
    public int MaxIvAgeMinutes => options.Value.MaxIvAgeMinutes!.Value;

    public string? CondorCostSheetId => string.IsNullOrWhiteSpace(options.Value.CondorCostSheetId) ? null : options.Value.CondorCostSheetId;
}
