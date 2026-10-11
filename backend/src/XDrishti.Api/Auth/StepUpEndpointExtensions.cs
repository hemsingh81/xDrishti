namespace XDrishti.Api.Auth;

internal static class StepUpEndpointExtensions
{
    /// <summary>
    /// Sensitive endpoint: answers <c>403 auth.step_up_required</c> unless the session's password re-entry window
    /// (<c>POST /api/auth/step-up</c>, 5 minutes) is open. Enforced by <see cref="StepUpMiddleware"/>, after authentication and CSRF checks.
    /// </summary>
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(StepUpRequiredMetadata.Instance).ProducesProblem(StatusCodes.Status403Forbidden);
}
