namespace XDrishti.Api.Endpoints;

internal static class EndpointDocumentation
{
    /// <summary>Documents the problem responses of a write endpoint: 400 (accounts.invalid with field errors) and, when relevant, 404 and 409.</summary>
    public static RouteHandlerBuilder ProducesRejections(this RouteHandlerBuilder builder, bool conflict, bool notFound = true)
    {
        builder.ProducesValidationProblem();
        if (notFound)
        {
            builder.ProducesProblem(StatusCodes.Status404NotFound);
        }

        return conflict ? builder.ProducesProblem(StatusCodes.Status409Conflict) : builder;
    }

    /// <summary>Documents the problem responses of an endpoint that calls the account's broker: 404, 409 and 502 (accounts.broker_error).</summary>
    public static RouteHandlerBuilder ProducesBrokerRejections(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
}
