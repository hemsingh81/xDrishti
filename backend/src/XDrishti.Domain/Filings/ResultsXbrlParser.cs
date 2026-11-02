using System.Globalization;
using System.Text;
using System.Xml;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The results parser (P15-03, ADR 0184), pure: it reads an <see cref="XmlReader"/> and creates none. The reader MUST come from the hardened factory of the infrastructure (no DTD, no resolver, no entity expansion, bounded size); this class adds its own bounds on depth and element
/// count and never follows a reference. A number is taken exactly as filed (no scaling, no rounding, no correction); a field the file does not carry, or carries in a way that cannot be read without a guess, is <c>unavailable</c> with a closed reason. The banking format is
/// recognised and answered <c>format_not_supported</c>.
/// </summary>
public static class ResultsXbrlParser
{
    private const string SchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

    public static ParsedResults Parse(XmlReader reader, DateOnly periodEnd)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            return new Run(reader, periodEnd).Execute();
        }
        catch (LimitException limit)
        {
            return ParsedResults.Reject(limit.Reason);
        }
        catch (XmlException)
        {
            return ParsedResults.Reject(ResultCodes.NotXml);
        }
    }

    private sealed class LimitException(string reason) : Exception
    {
        public string Reason { get; } = reason;
    }

    private sealed record Context(string? Start, string? End, string? Instant, IReadOnlyList<(string Dimension, string? Member)> Dimensions);

    private sealed record Raw(string Name, ResultsTaxonomyMap.Mapping Mapping, string? ContextRef, string? UnitRef, bool Nil, string Text);

    private sealed record Candidate(string Field, int Rank, string Span, string Basis, decimal? Value, string? Reason);

    private sealed class Run(XmlReader reader, DateOnly periodEnd)
    {
        private readonly Dictionary<string, Context?> _contexts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _units = new(StringComparer.Ordinal);
        private readonly List<Raw> _facts = [];
        private readonly List<string> _basisMarkers = [];
        private readonly List<string> _rounding = [];
        private readonly HashSet<string> _namespaces = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _flags = new(StringComparer.Ordinal);
        private int _elements;
        private bool _bank;

        public ParsedResults Execute()
        {
            var root = false;
            while (Next())
            {
                if (reader.NodeType == XmlNodeType.DocumentType)
                {
                    throw new LimitException(ResultCodes.DtdProhibited);
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!root)
                {
                    root = true;
                    if (reader.LocalName != "xbrl")
                    {
                        return ParsedResults.NotXbrl();
                    }

                    ReadRootNamespaces();
                }

                Inspect();
                switch (reader.LocalName)
                {
                    case "context":
                        ReadContext();
                        break;
                    case "unit":
                        ReadUnit();
                        break;
                    default:
                        ReadFactIfMapped();
                        break;
                }
            }

            return root ? Resolve() : ParsedResults.Reject(ResultCodes.NotXml);
        }

        private bool Next()
        {
            if (!reader.Read())
            {
                return false;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (++_elements > ResultsParser.MaxElements)
                {
                    throw new LimitException(ResultCodes.TooManyElements);
                }

                if (reader.Depth >= ResultsParser.MaxDepth)
                {
                    throw new LimitException(ResultCodes.TooDeep);
                }
            }

            return true;
        }

        private void ReadRootNamespaces()
        {
            if (!reader.HasAttributes)
            {
                return;
            }

            while (reader.MoveToNextAttribute())
            {
                if ((reader.Prefix == "xmlns" || reader.Name == "xmlns") && ResultsTaxonomyMap.IsBankingNamespace(reader.Value))
                {
                    _bank = true;
                }
            }

            reader.MoveToElement();
        }

        // Per element: a schema location is an external reference (never followed, and refused); a namespace is looked at once.
        private void Inspect()
        {
            if (reader.GetAttribute("schemaLocation", SchemaInstance) is not null || reader.GetAttribute("noNamespaceSchemaLocation", SchemaInstance) is not null)
            {
                throw new LimitException(ResultCodes.ExternalReference);
            }

            if (_namespaces.Add(reader.NamespaceURI) && ResultsTaxonomyMap.IsBankingNamespace(reader.NamespaceURI))
            {
                _bank = true;
            }
        }

        // The text directly inside the current element; nested elements are counted and skipped. Leaves the reader after the end tag.
        private string LeafText()
        {
            if (reader.IsEmptyElement)
            {
                return string.Empty;
            }

            var depth = reader.Depth;
            var text = new StringBuilder();
            while (Next())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (reader.Depth == depth + 1 && reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                {
                    if (text.Length <= ResultsParser.MaxLeafText)
                    {
                        text.Append(reader.Value);
                    }
                }
            }

            return text.ToString();
        }

        private static string Local(string qualified)
        {
            var colon = qualified.IndexOf(':', StringComparison.Ordinal);
            return (colon >= 0 ? qualified[(colon + 1)..] : qualified).Trim();
        }

        private void ReadContext()
        {
            var id = reader.GetAttribute("id");
            string? start = null, end = null, instant = null;
            var dimensions = new List<(string, string?)>();
            if (!reader.IsEmptyElement)
            {
                var depth = reader.Depth;
                while (Next())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    {
                        break;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    switch (reader.LocalName)
                    {
                        case "startDate":
                            start = LeafText();
                            break;
                        case "endDate":
                            end = LeafText();
                            break;
                        case "instant":
                            instant = LeafText();
                            break;
                        case "explicitMember":
                            var dimension = Local(reader.GetAttribute("dimension") ?? string.Empty);
                            dimensions.Add((dimension, Local(LeafText())));
                            break;
                        case "typedMember":
                            dimensions.Add((Local(reader.GetAttribute("dimension") ?? string.Empty), null));
                            LeafText();
                            break;
                    }
                }
            }

            if (id is null)
            {
                return;
            }

            // The same id twice is ambiguous: no fact may use it.
            _contexts[id] = _contexts.ContainsKey(id) ? null : new Context(start, end, instant, dimensions);
        }

        private void ReadUnit()
        {
            var id = reader.GetAttribute("id");
            var numerator = new List<string>();
            var denominator = new List<string>();
            var divide = false;
            var inDenominator = false;
            if (!reader.IsEmptyElement)
            {
                var depth = reader.Depth;
                while (Next())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    {
                        break;
                    }

                    if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "unitDenominator")
                    {
                        inDenominator = false;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    switch (reader.LocalName)
                    {
                        case "divide":
                            divide = true;
                            break;
                        case "unitDenominator":
                            inDenominator = !reader.IsEmptyElement;
                            break;
                        case "measure":
                            (inDenominator ? denominator : numerator).Add(Local(LeafText()));
                            break;
                    }
                }
            }

            if (id is null)
            {
                return;
            }

            var code = !divide && numerator is ["INR"] && denominator.Count == 0 ? ResultCodes.Inr
                : divide && numerator is ["INR"] && denominator is ["shares"] ? ResultCodes.InrPerShare
                : string.Empty;
            _units[id] = _units.ContainsKey(id) ? string.Empty : code;
        }

        private void ReadFactIfMapped()
        {
            var name = reader.LocalName;
            if (name is ResultsTaxonomyMap.BasisMarker or ResultsTaxonomyMap.RoundingMarker)
            {
                var marker = LeafText().Trim();
                (name == ResultsTaxonomyMap.BasisMarker ? _basisMarkers : _rounding).Add(marker);
                return;
            }

            var mapping = ResultsTaxonomyMap.Find(name);
            if (mapping is null)
            {
                return;
            }

            var contextRef = reader.GetAttribute("contextRef");
            var unitRef = reader.GetAttribute("unitRef");
            var nil = string.Equals(reader.GetAttribute("nil", SchemaInstance), "true", StringComparison.Ordinal);
            _facts.Add(new Raw(name, mapping, contextRef, unitRef, nil, LeafText().Trim()));
        }

        private void Count(string flag) => _flags[flag] = _flags.GetValueOrDefault(flag) + 1;

        private ParsedResults Resolve()
        {
            if (_bank)
            {
                return ParsedResults.NoFactsBecause(ResultCodes.FormatNotSupported, _flags);
            }

            var fileBasis = FileBasis();
            var scaleKnown = _rounding.TrueForAll(r => r.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() is "actual" or "absolute" or "rupees" or "inr");
            var candidates = new List<Candidate>();
            foreach (var raw in _facts)
            {
                if (raw.Nil)
                {
                    Count("nil_skipped");
                    continue;
                }

                if (raw.ContextRef is null || !_contexts.TryGetValue(raw.ContextRef, out var context) || context is null)
                {
                    Count("unknown_context");
                    continue;
                }

                var span = SpanRule.Classify(context.Start, context.End, context.Instant, periodEnd);
                if (span is null)
                {
                    Count("unknown_span");
                    continue;
                }

                if (!TryBasis(context, fileBasis, out var basis, out var otherDimension))
                {
                    Count(otherDimension ? "dimension_skipped" : "basis_unknown");
                    continue;
                }

                candidates.Add(Evaluate(raw, span, basis!, scaleKnown));
            }

            var keys = candidates.Select(c => (c.Span, c.Basis)).Distinct().OrderBy(k => Order(ResultCodes.Spans, k.Span)).ThenBy(k => Order(ResultCodes.Bases, k.Basis)).ToList();
            if (keys.Count == 0)
            {
                return ParsedResults.NoFactsBecause(_flags.GetValueOrDefault("basis_unknown") > 0 ? ResultCodes.BasisUnknown : ResultCodes.NothingMapped, _flags);
            }

            var facts = new List<ParsedFact>();
            foreach (var (span, basis) in keys)
            {
                var group = new List<ParsedFact>();
                foreach (var field in ResultCodes.Fields)
                {
                    group.Add(Choose(field, span, basis, candidates));
                }

                foreach (var flag in FactSanity.Flags(group))
                {
                    Count(flag);
                }

                facts.AddRange(group);
            }

            return new ParsedResults(ResultCodes.Parsed, null, facts, _flags);
        }

        private string? FileBasis()
        {
            var found = _basisMarkers.Select(m => m.ToLowerInvariant()).Where(m => m is "consolidated" or "standalone").Distinct().ToList();
            return found.Count == 1 ? found[0] : null;
        }

        private static int Order(IReadOnlyList<string> list, string item) => list.ToList().IndexOf(item);

        // The basis comes from a basis-axis member of the context, else from the file's marker; any other dimension (a segment) skips the fact.
        private static bool TryBasis(Context context, string? fileBasis, out string? basis, out bool otherDimension)
        {
            basis = null;
            otherDimension = false;
            var fromAxis = new List<string>();
            foreach (var (dimension, member) in context.Dimensions)
            {
                if (dimension == ResultsTaxonomyMap.BasisAxis && member == ResultsTaxonomyMap.ConsolidatedMember)
                {
                    fromAxis.Add(ResultCodes.Consolidated);
                }
                else if (dimension == ResultsTaxonomyMap.BasisAxis && member == ResultsTaxonomyMap.StandaloneMember)
                {
                    fromAxis.Add(ResultCodes.Standalone);
                }
                else
                {
                    otherDimension = true;
                    return false;
                }
            }

            basis = fromAxis.Count == 0 ? fileBasis : fromAxis.Distinct().Count() == 1 ? fromAxis[0] : null;
            return basis is not null;
        }

        private Candidate Evaluate(Raw raw, string span, string basis, bool scaleKnown)
        {
            var field = raw.Mapping.Field;
            Candidate Bad(string reason) => new(field, raw.Mapping.Rank, span, basis, null, reason);
            if (raw.Text.Length > ResultsParser.MaxLeafText)
            {
                return Bad(ResultCodes.BadValue);
            }

            if (field == ResultCodes.Audited)
            {
                var word = new string([.. raw.Text.Where(char.IsLetter)]).ToLowerInvariant();
                return word switch
                {
                    "audited" => new Candidate(field, raw.Mapping.Rank, span, basis, 1m, null),
                    "unaudited" => new Candidate(field, raw.Mapping.Rank, span, basis, 0m, null),
                    _ => Bad(ResultCodes.BadValue),
                };
            }

            var unit = raw.UnitRef is not null && _units.TryGetValue(raw.UnitRef, out var code) ? code : string.Empty;
            if (unit != ResultCodes.UnitOf(field))
            {
                return Bad(ResultCodes.UnitUnknown);
            }

            if (unit == ResultCodes.Inr && !scaleKnown)
            {
                return Bad(ResultCodes.ScaleUnknown);
            }

            return decimal.TryParse(raw.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)
                ? new Candidate(field, raw.Mapping.Rank, span, basis, value, null)
                : Bad(ResultCodes.BadValue);
        }

        private ParsedFact Choose(string field, string span, string basis, List<Candidate> all)
        {
            var mine = all.Where(c => c.Field == field && c.Span == span && c.Basis == basis).ToList();
            if (mine.Count == 0)
            {
                return ParsedFact.Missing(field, span, basis, ResultCodes.NotInFile);
            }

            var rank = mine.Min(c => c.Rank);
            var chosen = mine.Where(c => c.Rank == rank).ToList();
            var bad = chosen.Where(c => c.Value is null).Select(c => c.Reason!).OrderBy(r => r, StringComparer.Ordinal).FirstOrDefault();
            if (bad is not null)
            {
                return ParsedFact.Missing(field, span, basis, bad);
            }

            if (chosen.Select(c => c.Value!.Value).Distinct().Count() > 1)
            {
                return ParsedFact.Missing(field, span, basis, ResultCodes.ConflictingFacts);
            }

            if (rank == ResultsTaxonomyMap.FallbackRank)
            {
                Count("pat_from_profit_for_period");
            }

            return ParsedFact.Of(field, span, basis, chosen[0].Value!.Value);
        }
    }
}
