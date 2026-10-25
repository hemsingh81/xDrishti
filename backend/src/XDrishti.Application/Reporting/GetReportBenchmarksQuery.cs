using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The buy-and-hold of baskets and the benchmark index over one window (P8-07). All fields are the raw text of the request and are checked by the handler.</summary>
/// <param name="Basket">Basket ids, comma separated.</param>
/// <param name="From">First India date, yyyy-MM-dd; default from the default run set.</param>
/// <param name="To">Last India date, yyyy-MM-dd; default from the default run set.</param>
/// <param name="MembersAsOf">India date of the members, yyyy-MM-dd; default the newest contributing run, else today.</param>
public sealed record GetReportBenchmarksQuery(string? Basket, string? From, string? To, string? MembersAsOf) : IQuery<Result<ReportBenchmarksView>>;
