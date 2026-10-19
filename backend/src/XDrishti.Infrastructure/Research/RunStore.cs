using Npgsql;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The result store behind <see cref="IRunStore"/> (spec P3-10, ADR 0045): <c>research.sim_runs</c> and <c>research.sim_trades</c>. The write is one transaction on one connection (an insert of the header that does nothing on a stored key, then a binary
/// copy of the trades, then a count check); the reads project without tracking or read with a plain reader. The only SQL that writes the research tables is here and in the migration. Never logs a trade, the record text or the strategy.
/// </summary>
internal sealed partial class RunStore(NpgsqlDataSource dataSource, XDrishtiDbContext db, TimeProvider time) : IRunStore;
