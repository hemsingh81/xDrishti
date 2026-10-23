using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A registered model with its id and its current role (the latest designation).</summary>
/// <param name="Id">The registry id.</param>
/// <param name="Role">The current role.</param>
/// <param name="Model">What was registered.</param>
public sealed record StoredModel(long Id, ModelRole Role, ModelToRegister Model);
