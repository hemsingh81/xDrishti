namespace XDrishti.Domain.LiveTrading;

/// <summary>Against what a slippage was measured (spec P11-02 decision 1). The two kinds are never pooled; <see cref="Unavailable"/> means there was no reference and every slippage figure is unavailable.</summary>
public static class ReferenceKind
{
    public const string TicketLevel = "ticket_level";
    public const string PlanGrade = "plan_grade";
    public const string Unavailable = "unavailable";

    public static IReadOnlyList<string> All { get; } = [TicketLevel, PlanGrade, Unavailable];
}
