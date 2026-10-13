using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace XDrishti.Api.Infrastructure;

/// <summary>Documents the extension members every error response carries, so the generated client can use them.</summary>
internal static class ProblemDetailsSchema
{
    public static Task AddExtensions(OpenApiSchema schema, Microsoft.AspNetCore.OpenApi.OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(ProblemDetails) || context.JsonTypeInfo.Type == typeof(HttpValidationProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["code"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "Stable machine-readable error code, e.g. auth.invalid_credentials, auth.locked, auth.session_expired, auth.csrf_invalid, auth.step_up_required, accounts.not_found, accounts.invalid (field errors in \"errors\"), accounts.conflict, accounts.app_not_configured, accounts.broker_error (502), accounts.connect_mismatch, accounts.consent_limit (429), accounts.connect_expired, accounts.connect_cancelled, portfolio.not_connected, portfolio.sync_running, portfolio.confirmation_required and portfolio.confirmation_stale (409, with listedHoldings, openHoldings and wouldClose), portfolio.conflict (409), portfolio.invalid, portfolio.not_found, portfolio.lot_from_trade (409), portfolio.preview_stale (409, with report), services.not_found (404), services.invalid (400, with issues), services.revision_changed (409), services.already_running (409), services.read_only (409).",
            };
            schema.Properties["retryAfterSeconds"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "Present on auth.locked, auth.rate_limited and accounts.consent_limit: seconds until a new attempt may succeed.",
            };
            schema.Properties["openHoldings"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "Present on portfolio.confirmation_required and portfolio.confirmation_stale: how many holdings the account has open.",
            };
            schema.Properties["listedHoldings"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "Present on portfolio.confirmation_required and portfolio.confirmation_stale: how many holdings Dhan listed.",
            };
            schema.Properties["issues"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Description = "Present on services.invalid: every error of the refused schedule - the service it is about (serviceId; absent for the service of the request), the field (a dotted path like the request, e.g. schedule.time, retries, parameters.x), severity (error), a stable code and the text.",
                Items = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["serviceId"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                        ["field"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["severity"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                    Required = new HashSet<string> { "field", "severity", "code", "message" },
                },
            };
            schema.Properties["report"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Present on portfolio.preview_stale: the fresh preview of the file, in the shape of the response of POST /api/portfolio/trades/preview (TradePreviewReport).",
            };
            schema.Properties["wouldClose"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "Present on portfolio.confirmation_required: how many of them the sync would close because Dhan no longer lists them.",
            };
        }

        return Task.CompletedTask;
    }
}
