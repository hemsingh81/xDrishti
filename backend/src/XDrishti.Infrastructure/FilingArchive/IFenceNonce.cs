namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>The random token of a fence (P15-08): 16 hexadecimal characters per request, scripted in tests.</summary>
internal interface IFenceNonce
{
    string Next();
}
