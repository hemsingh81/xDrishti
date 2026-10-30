namespace XDrishti.Api.Endpoints;

/// <summary>The owner's tap: the hash of the text the card showed, the password when the ticket asks for it, and a reason when Soft checks did not pass. The actor is never taken from the body.</summary>
internal sealed record ConfirmTicketBody(string? TextHash, string? Password, string? Reason);

/// <summary>The owner's skip, with an optional plain-text reason of at most 200 characters.</summary>
internal sealed record SkipTicketBody(string? Reason);

/// <summary>The owner's daily unlock of one account: a fresh password for this act.</summary>
internal sealed record DayUnlockBody(Guid? AccountId, string? Password);
