namespace XDrishti.Application.Abstractions.Planning;

/// <summary>How tickets are charged when graded (P7-13). Unset means gross, and every grade says so.</summary>
public interface IGradeSettings
{
    /// <summary>The stored charge sheet id (<c>Planning:Grading:CostSheetId</c>), or null for gross.</summary>
    string? CostSheetId { get; }

    /// <summary>The simulated capital in rupees (<c>Planning:Grading:Capital</c>).</summary>
    decimal Capital { get; }
}
