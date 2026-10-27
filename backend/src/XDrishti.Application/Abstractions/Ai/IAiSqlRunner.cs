namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// Runs one pre-checked <c>SELECT</c> under the SQL role: a read-only transaction, a cursor, a fetch of one row more than <c>maxRows</c> (to know whether it was cut), a statement timeout of <c>timeout</c> (the call is stopped half a second later whatever the database does), then rollback. The privilege of the role is the boundary, not the text. Never throws for a database refusal.
/// </summary>
public interface IAiSqlRunner
{
    Task<SqlRunOutcome> RunAsync(string statement, int maxRows, TimeSpan timeout, CancellationToken cancellationToken);
}
