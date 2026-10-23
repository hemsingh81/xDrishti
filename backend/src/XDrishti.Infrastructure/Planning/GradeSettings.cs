using System.Globalization;
using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Research;

namespace XDrishti.Infrastructure.Planning;

/// <summary>Reads <c>Planning:Grading:CostSheetId</c> and <c>Capital</c> (P7-13). Unset or empty means gross; a capital that is not a positive number means the default.</summary>
internal sealed class GradeSettings(IConfiguration configuration) : IGradeSettings
{
    public string? CostSheetId => string.IsNullOrWhiteSpace(configuration["Planning:Grading:CostSheetId"]) ? null : configuration["Planning:Grading:CostSheetId"]!.Trim();

    public decimal Capital => decimal.TryParse(configuration["Planning:Grading:Capital"], NumberStyles.Number, CultureInfo.InvariantCulture, out var c) && c > 0 ? c : CostChoice.DefaultCapital;
}
