namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The stored data the assistant's typed tools read (spec P9-05), only from the reviewed <c>aiv</c> views, parameterised, read only. Portfolio rows are read only after the database's switch has been checked. One method per tool; none takes a table or column name from the model.
/// </summary>
public interface IAiDataReader
{
    Task<AiToolData> ListLabRunsAsync(string? strategy, int limit, CancellationToken cancellationToken);

    Task<AiToolData> GetPlanAsync(string? date, string? accountLabel, CancellationToken cancellationToken);

    Task<AiToolData> GetCellAsync(string? key, string? strategy, string? timeframe, string? side, CancellationToken cancellationToken);

    Task<AiToolData> GetPortfolioAsync(string groupBy, bool includeClosed, CancellationToken cancellationToken);

    Task<AiToolData> GetBasketAsync(string name, CancellationToken cancellationToken);

    Task<AiToolData> GetInstrumentAsync(string symbol, int bars, CancellationToken cancellationToken);

    Task<AiToolData> SearchNotesAsync(string query, string? from, string? to, int limit, CancellationToken cancellationToken);

    Task<AiToolData> GetLearningAsync(string kind, string? status, int limit, CancellationToken cancellationToken);

    Task<AiToolData> GetSystemStatusAsync(CancellationToken cancellationToken);
}
