namespace XDrishti.Api.Auth;

/// <summary>
/// The narrow service credential of the assistant's tool server (BK-12b, ADR 0267): a bearer token (<c>Api:ServiceTokens:Mcp</c>, secret file <c>mcp_api_token</c>) that only the endpoints marked <c>AssistantBasketRead</c> accept. It is not the default scheme, so the fallback policy
/// (an owner session) still guards every other route and the token opens nothing else. The principal has no owner claim and no session.
/// </summary>
internal static class ServiceReadAuthentication
{
    public const string Scheme = "xd-service-read";

    /// <summary>An owner session or the service principal; used by exactly the two basket report routes.</summary>
    public const string AssistantBasketReadPolicy = "AssistantBasketRead";

    /// <summary>An owner session or the service principal; used by exactly the six evidence GETs (replay list, run and equity; readiness scorecard list and card; shadow summary), AI-01, ADR 0280.</summary>
    public const string AssistantEvidenceReadPolicy = "AssistantEvidenceRead";

    /// <summary>The one claim of the service principal: the name of the service. Never a name identifier, e-mail or session id.</summary>
    public const string ServiceClaim = "xd:service";

    public const string McpService = "mcp";
}
