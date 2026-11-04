namespace XDrishti.Application.Lab;

/// <summary>The pre-registration of a strategy version as served (spec CF-06, D1): five plain texts (never markup), the hash of their canonical text, who wrote it and when. It is frozen once written.</summary>
public sealed record RegistrationView(string StrategyId, int Version, string Counterparty, string ParameterSigns, string Applicability, string ExpectedEdge, string Falsifier, string TextHash, DateTimeOffset CreatedAt, string Actor);
