using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Strength;

/// <summary>Structured log events of the relative-strength step (event ids 7420 and 7421: unique across the solution, asserted by the architecture tests). Counts and codes only: no symbol per row.</summary>
internal static partial class StrengthLog
{
    [LoggerMessage(EventId = 7420, Level = LogLevel.Information, Message = "Relative strength computed: {Days} days written, {Existing} already had rows, {Rows} rows, {Ms} ms")]
    public static partial void Computed(ILogger logger, int days, int existing, int rows, long ms);

    [LoggerMessage(EventId = 7421, Level = LogLevel.Warning, Message = "Relative strength: a prerequisite is missing ({Missing}); nothing was written")]
    public static partial void PrerequisiteMissing(ILogger logger, string missing);
}
