namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Reads and compiles a strategy text without storing anything (the same library as the Lab's validate).</summary>
public interface IAiLabValidator
{
    LabValidation Validate(string yaml);
}
