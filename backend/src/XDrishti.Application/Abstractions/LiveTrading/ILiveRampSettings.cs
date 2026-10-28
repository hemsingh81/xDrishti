namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>The owner-fixed ramp factors (<c>Live:Ramp:StepFactors</c>, default 0.25, 0.5, 1.0): validated at start, never above the ceiling of the step.</summary>
public interface ILiveRampSettings
{
    IReadOnlyList<double> StepFactors { get; }
}
