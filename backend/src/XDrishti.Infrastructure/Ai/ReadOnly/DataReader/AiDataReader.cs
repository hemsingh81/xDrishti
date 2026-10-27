using System.Globalization;
using Npgsql;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

/// <summary>
/// The stored data behind the assistant's typed tools (spec P9-05, ADR 0139): raw, parameterised Npgsql over the reviewed <c>aiv</c> views under the read login, one file per tool family. No SQL text comes from the model; a value from the model is only ever a parameter. The portfolio views
/// answer no row while the database's switch is off, and the portfolio tool checks the switch first anyway. Never logs a value.
/// </summary>
internal sealed partial class AiDataReader(AiDataSources sources, IAiContext context, TimeProvider time) : IAiDataReader
{
    private const string InvalidArguments = "tool.invalid_arguments";

    /// <summary>Runs one SELECT and returns its rows with every value in the form the envelope writes: dates and moments as text, arrays joined, decimals as numbers.</summary>
    private async Task<List<object?[]>> QueryAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        try
        {
            await using var command = sources.Read.CreateCommand(sql);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            var rows = new List<object?[]>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : Normalise(reader.GetValue(i));
                }

                rows.Add(row);
            }

            return rows;
        }
        catch (Exception failure) when (failure is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            throw new AiDatabaseUnavailableException("The read-only database is not available.", failure);
        }
    }

    private static object? Normalise(object value) => value switch
    {
        DateTime d when d.TimeOfDay == TimeSpan.Zero && d.Kind == DateTimeKind.Unspecified => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => (double)m,
        string[] list => string.Join("; ", list),
        Guid g => g.ToString("D"),
        _ => value,
    };

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDay(string? text, out DateOnly date) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private DateOnly TodayInIndia() => DateOnly.FromDateTime(time.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);
}
