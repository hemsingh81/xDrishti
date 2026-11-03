namespace XDrishti.Application.Abstractions.Research;

/// <summary>The version of the simulation engine in force (P15-12): the one read a version check needs, without the engine's run members.</summary>
public interface ISimulationEngineVersion
{
    string EngineVersion { get; }
}
