using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>The stored plan flags as JSON (spec P8-14). A value that cannot be read is no flags (the screen then reads the basis text, labelled), never a failed plan read.</summary>
internal static partial class PlanFlagsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write(PlanBasisFlags flags) => JsonSerializer.Serialize(flags, Options);

    public static PlanBasisFlags? Read(string json, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<PlanBasisFlags>(json, Options);
        }
        catch (JsonException)
        {
            Unreadable(logger);
            return null;
        }
    }

    [LoggerMessage(EventId = 4796, Level = LogLevel.Warning, Message = "plans.flags_unreadable: the stored plan flags are not valid JSON of the expected shape; read as no flags")]
    private static partial void Unreadable(ILogger logger);
}
