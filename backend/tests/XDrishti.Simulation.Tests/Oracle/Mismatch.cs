namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>One field on which the oracle and the engine differ (beyond the tolerance of D5): the field and both values as text.</summary>
internal sealed record Mismatch(string Field, string Oracle, string Engine)
{
    public override string ToString() => $"{Field}: oracle {Oracle}, engine {Engine}";
}
