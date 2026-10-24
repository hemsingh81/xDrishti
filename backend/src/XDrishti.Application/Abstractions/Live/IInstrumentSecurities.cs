namespace XDrishti.Application.Abstractions.Live;

/// <summary>A listed share or ETF with Dhan's security id in the NSE equity segment, as the feed and the pre-open read need it.</summary>
public sealed record InstrumentSecurity(string Symbol, Guid InstrumentId, string SecurityId);

/// <summary>The market module's side of the live feed for plan tickets (spec P7-11): finds shares by canonical symbol. Retired instruments, ambiguous symbols and instruments without a Dhan NSE equity id are absent from the answer.</summary>
public interface IInstrumentSecurities
{
    /// <summary>The instruments that match; a symbol that does not match one live instrument is simply absent.</summary>
    ValueTask<IReadOnlyDictionary<string, InstrumentSecurity>> FindAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken);
}
