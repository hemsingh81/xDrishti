using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionPhaseCheck;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

internal sealed class OptionsPhaseCheckSettings(IOptions<OptionsPhaseCheckOptions> options) : IOptionsPhaseCheckSettings
{
    public int StaleAfterDays => options.Value.StaleAfterDays!.Value;
}
