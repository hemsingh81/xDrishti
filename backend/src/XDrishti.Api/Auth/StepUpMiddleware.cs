using XDrishti.Application.Auth;

namespace XDrishti.Api.Auth;

/// <summary>
/// Endpoints marked with <see cref="StepUpRequiredMetadata"/> need an elevated session; otherwise 403 <c>auth.step_up_required</c>.
/// Runs after authorization, so an unauthenticated request still gets its 401 first; a request without a session is never elevated. The elevation was read together with the session
/// during authentication, so no extra database round trip happens here.
/// </summary>
internal sealed class StepUpMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<StepUpRequiredMetadata>() is not null
            && context.GetVerifiedSession() is not { IsElevated: true })
        {
            await ProblemResults.WriteAsync(context, ProblemResults.Create(
                StatusCodes.Status403Forbidden, AuthErrors.StepUpRequired.Message, AuthErrors.StepUpRequiredCode));
            return;
        }

        await next(context);
    }
}
