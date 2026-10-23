namespace XDrishti.Application.Abstractions.Planning;

/// <summary>Which plan builder the nightly uses (P7-04, ADR 0088). Off by default: the empty skeleton stays the builder until the owner turns the selection builder on (<c>Planning:Builder:UseSelection</c>).</summary>
public interface IPlanBuilderSettings
{
    /// <summary>True to use the selection builder; false (the default) for the null builder.</summary>
    bool UseSelection { get; }
}
