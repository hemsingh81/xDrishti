using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>Reads <c>Planning:Builder:UseSelection</c> (P7-04, ADR 0088). Missing or not a boolean means false: the null builder stays.</summary>
internal sealed class PlanBuilderSettings(IConfiguration configuration) : IPlanBuilderSettings
{
    public bool UseSelection => configuration.GetValue<bool?>("Planning:Builder:UseSelection") ?? false;
}
