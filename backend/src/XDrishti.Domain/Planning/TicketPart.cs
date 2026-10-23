namespace XDrishti.Domain.Planning;

/// <summary>One condition of a setup as classified at the close of D.</summary>
/// <param name="Path">Where it is in the strategy, for example <c>buy.trigger[1]</c>.</param>
/// <param name="Text">The condition as written.</param>
/// <param name="Truth">True or False when known at the close, Unknown when it is knowable only tomorrow or its data is not ready.</param>
/// <param name="KnownAtClose">Whether the part reads only closed daily (or longer) data, so that Unknown means data not ready.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record TicketPart(string Path, string Text, SetupTruth Truth, bool KnownAtClose, string Reason);
