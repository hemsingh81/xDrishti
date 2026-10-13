namespace XDrishti.Domain.Platform.Services;

/// <summary>The kind of a service parameter. Sent as integer · number · boolean (<see cref="ParameterKinds"/>).</summary>
public enum ParameterKind
{
    Whole,
    Fractional,
    Switch,
}
