using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace XDrishti.Api.Infrastructure;

/// <summary>Documents the extension members every error response carries, so the generated client can use them.</summary>
internal static class ProblemDetailsSchema
{
    public static Task AddExtensions(OpenApiSchema schema, Microsoft.AspNetCore.OpenApi.OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(ProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Stable machine-readable error code, e.g. auth.invalid_credentials, auth.locked, auth.session_expired, auth.csrf_invalid.",
            };
            schema.Properties["retryAfterSeconds"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "Present on auth.locked and auth.rate_limited: seconds until a new attempt may succeed.",
            };
        }

        return Task.CompletedTask;
    }
}
