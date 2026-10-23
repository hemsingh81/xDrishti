using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A registered model with its id and its current role (the latest designation).</summary>
/// <param name="Id">The registry id.</param>
/// <param name="Role">The current role.</param>
/// <param name="Model">What was registered. In a list the report, the parameters and the arrays are empty.</param>
/// <param name="RequiredTrials">The distinct models of its feature version registered up to it (itself included): the trials a champion's verdict must allow for.</param>
public sealed record StoredModel(long Id, ModelRole Role, ModelToRegister Model, int RequiredTrials);
