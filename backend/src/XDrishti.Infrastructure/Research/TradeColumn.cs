using System.Data.Common;
using NpgsqlTypes;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// One column of <c>research.sim_trades</c> beyond its four key columns (spec P3-10, D3 and D8): its name, the Domain value it carries, how it is written to the binary copy, how it is read back and the closed set of names when it is an enum.
/// The one list of these (<see cref="TradeColumns"/>) drives the model (so the migration), the copy, the reads and the generated checks, so the order and the set cannot drift.
/// </summary>
/// <param name="Name">The column name (snake_case).</param>
/// <param name="ClrType">The type of the value as the model has it (nullable value types for nullable columns).</param>
/// <param name="DbType">The type of the binary copy.</param>
/// <param name="SqlType">The PostgreSQL type of the column, as the migration spells it.</param>
/// <param name="Nullable">Whether the column accepts null.</param>
/// <param name="Get">The value of a trade (null for a null column), in the type the copy expects.</param>
/// <param name="Read">The value of a column from a reader, null for a null.</param>
/// <param name="AllowedNames">The closed set of lower-case names for an enum column, otherwise null.</param>
internal sealed record TradeColumn(string Name, Type ClrType, NpgsqlDbType DbType, string SqlType, bool Nullable, Func<RunTrade, object?> Get, Func<DbDataReader, int, object?> Read, IReadOnlyList<string>? AllowedNames);
