namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary><c>Selection:ListFilters:Enabled</c> (DATA-01, ADR 0274), default false. Off: the plan and the replay do not read the dated lists and the ban and surveillance filters answer exactly as they did before.</summary>
public interface IListFilterSettings
{
    bool Enabled { get; }
}
