using System.Text.Json;
using System.Text.Json.Serialization;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The body of a schedule save or preview: everything the dialog sets, all of it required (a body that leaves a field out is a 400, so a missing switch is never read as "off"). <c>parameters</c> are the values by name
/// (those left out keep the schema's default); <c>revision</c> is the one read with the service (0 for a service that has no stored choices yet, also the default) and is only checked by a save.
/// </summary>
public sealed record ServiceScheduleInput(
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] ScheduleSpec Schedule,
    [property: JsonRequired] int Retries,
    [property: JsonRequired] int TimeoutMinutes,
    [property: JsonRequired] bool CatchUp,
    [property: JsonRequired] bool NotifyOnFailure,
    IReadOnlyDictionary<string, JsonElement>? Parameters = null,
    long Revision = 0);
