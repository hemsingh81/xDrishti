namespace XDrishti.Api.Auth;

/// <summary>Endpoint marker: the session must have re-entered the password recently (see <see cref="StepUpEndpointExtensions.RequireStepUp{TBuilder}"/>).</summary>
internal sealed class StepUpRequiredMetadata
{
    public static readonly StepUpRequiredMetadata Instance = new();

    private StepUpRequiredMetadata()
    {
    }
}
