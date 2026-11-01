using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OptionQuality;

/// <summary>The stored quality verdicts of one underlying (a canonical symbol such as <c>NSE:NIFTY</c>, a bare ticker gets <c>NSE:</c>) from <paramref name="From"/> to <paramref name="To"/> (default: the last 30 days up to today, India time).</summary>
public sealed record GetOptionCoverageQuery(string? Underlying, DateOnly? From, DateOnly? To) : IQuery<Result<OptionCoverageView>>;
