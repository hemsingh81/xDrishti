namespace XDrishti.Api.Endpoints;

/// <summary>A feature's HTTP surface. One module per feature area; keeps Program.cs free of routes.</summary>
internal interface IEndpointModule
{
    void Map(IEndpointRouteBuilder api);
}
