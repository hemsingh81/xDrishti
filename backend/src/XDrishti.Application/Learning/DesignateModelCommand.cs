using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Gives a registered model a role (P6-07): appends a designation. Only an Eligible model may be named champion; naming one retires the previous champion.</summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Role">The role.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who asks.</param>
public sealed record DesignateModelCommand(long ModelId, ModelRole Role, string Reason, string Actor) : ICommand;
