using System.Xml;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The only place an <see cref="XmlReader"/> is created for a filing (P15-03, ADR 0184; pinned by an architecture test). A filing is untrusted input: a DTD is prohibited (so no entity can be declared or expanded and nothing is fetched), there is no resolver (no file or network access
/// whatever the document says), comments and processing instructions are dropped, characters are checked and the document size is bounded.
/// </summary>
internal static class SafeXml
{
    public static XmlReader Create(TextReader text, long maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0, // moot: with the DTD prohibited no entity can exist
            MaxCharactersInDocument = maxCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = true,
            ValidationType = ValidationType.None,
            CloseInput = false,
        };
        return XmlReader.Create(text, settings);
    }
}
