using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary>Draws the labelling sample of stored announcements broadcast from <see cref="From"/> to <see cref="To"/> (both India dates, inclusive) with <see cref="Seed"/> (P15-07).</summary>
public sealed record SampleAnnouncementsCommand(DateOnly From, DateOnly To, long Seed) : ICommand;
