using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Auth;

/// <summary>RFC 7807 problem details with a stable machine-readable <c>code</c> (and <c>retryAfterSeconds</c> when relevant).</summary>
internal static class ProblemResults
{
    public static ProblemDetails Create(int status, string title, string code, int? retryAfterSeconds = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        if (retryAfterSeconds is { } seconds)
        {
            problem.Extensions["retryAfterSeconds"] = seconds;
        }

        return problem;
    }

    public static ProblemHttpResult From(Error error, HttpContext context)
    {
        var status = error.Kind switch
        {
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Locked => StatusCodes.Status423Locked,
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        if (error.RetryAfterSeconds is { } seconds)
        {
            context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return TypedResults.Problem(Create(status, error.Message, error.Code, error.RetryAfterSeconds));
    }

    /// <summary>For middleware and auth handlers that write the response themselves.</summary>
    public static async ValueTask WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await service.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
