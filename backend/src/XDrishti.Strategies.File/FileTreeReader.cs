using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace XDrishti.Strategies.File;

/// <summary>
/// Reads untrusted YAML text into the generic tree (P3-02c D3) through the parser's events only. Every limit is checked as the events arrive, so an attacker's text is refused after the first event that breaks one and the memory in use
/// is bounded by the limits. Refused by name: anchors and aliases, tags and merge keys, a second document, a duplicate key. Reads a string once; follows no path, URL, include or tag.
/// </summary>
internal sealed class FileTreeReader
{
    private readonly FileLimits _limits;
    private int _nodes;

    private FileTreeReader(FileLimits limits) => _limits = limits;

    /// <summary>The tree, or null with exactly one problem in <paramref name="problems"/>.</summary>
    public static FileNode? Read(string text, FileLimits limits, List<FileProblem> problems)
    {
        if (Encoding.UTF8.GetByteCount(text) > limits.MaxBytes)
        {
            problems.Add(Problem(default, FileCodes.TooLarge, $"The file is larger than {limits.MaxBytes} bytes."));
            return null;
        }

        foreach (var (c, index) in text.Select((c, i) => (c, i)).Where(p => IsForbidden(p.c)))
        {
            problems.Add(LocateOf(text, index, FileCodes.BadCharacter, $"The file contains the control character U+{(int)c:X4}; only tab and line breaks are allowed."));
            return null;
        }

        try
        {
            return new FileTreeReader(limits).ReadDocument(text);
        }
        catch (FileTreeException e)
        {
            problems.Add(e.Problem);
            return null;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or OperationCanceledException))
        {
            // the parser library throws its own YamlException for bad syntax and, on some malformed text, a plain framework exception; both are the same answer: the text is not valid YAML
            var start = (e as YamlException)?.Start ?? Mark.Empty;
            var end = (e as YamlException)?.End ?? Mark.Empty;
            problems.Add(new FileProblem(string.Empty, (int)start.Line, (int)start.Column, (int)end.Line, (int)end.Column, FileCodes.YamlSyntax, ProblemSeverity.Error, "The text is not valid YAML: " + Plain(e.Message)));
            return null;
        }
    }

    private static bool IsForbidden(char c) => (c < ' ' && c is not ('\t' or '\n' or '\r')) || c == '\u007f' || c == '￾' || c == '￿';

    private static FileProblem LocateOf(string text, int index, string code, string message)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new FileProblem(string.Empty, line, column, line, column + 1, code, ProblemSeverity.Error, message);
    }

    private static string Plain(string message) => message.Replace('\n', ' ').Replace('\r', ' ');

    private static FileProblem Problem(FilePosition at, string code, string message) => new(string.Empty, at.Line, at.Column, at.EndLine, at.EndColumn, code, ProblemSeverity.Error, message);

    private static FilePosition At(ParsingEvent e) => new((int)e.Start.Line, (int)e.Start.Column, (int)e.End.Line, (int)e.End.Column);

    private FileNode? ReadDocument(string text)
    {
        var parser = new Parser(new StringReader(text));
        FileNode? root = null;
        var documents = 0;
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case DocumentStart start:
                    if (++documents > 1)
                    {
                        throw new FileTreeException(Problem(At(start), FileCodes.MultipleDocuments, "The file has more than one YAML document."));
                    }

                    break;
                case StreamEnd or DocumentEnd or StreamStart:
                    break;
                default:
                    root = ReadNode(parser, parser.Current!, 1);
                    break;
            }
        }

        return root ?? throw new FileTreeException(Problem(default, FileCodes.Empty, "The file is empty."));
    }

    private void CountNode(ParsingEvent at)
    {
        if (++_nodes > _limits.MaxNodes)
        {
            throw new FileTreeException(Problem(At(at), FileCodes.TooManyNodes, $"The file has more than {_limits.MaxNodes} nodes."));
        }
    }

    // The scalar length limit is checked after the parser has built the value; the real bound on any scalar is the byte cap on the whole text (MaxBytes), checked before parsing.
    private FileNode ReadNode(Parser parser, ParsingEvent current, int depth)
    {
        CountNode(current);

        switch (current)
        {
            case AnchorAlias alias:
                throw new FileTreeException(Problem(At(alias), FileCodes.AliasRefused, "Anchors and aliases are not allowed."));
            case Scalar scalar:
                Refuse(scalar.Anchor, scalar.Tag, scalar);
                if (scalar.Value.Length > _limits.MaxScalarLength)
                {
                    throw new FileTreeException(Problem(At(scalar), FileCodes.ScalarTooLong, $"A value is longer than {_limits.MaxScalarLength} characters."));
                }

                return new ScalarNode(At(scalar), scalar.Value, scalar.Style != ScalarStyle.Plain, scalar.Style is ScalarStyle.Literal or ScalarStyle.Folded);
            case SequenceStart sequence:
                Refuse(sequence.Anchor, sequence.Tag, sequence);
                return ReadSequence(parser, sequence, depth);
            case MappingStart mapping:
                Refuse(mapping.Anchor, mapping.Tag, mapping);
                return ReadMapping(parser, mapping, depth);
            default:
                throw new FileTreeException(Problem(At(current), FileCodes.YamlSyntax, "Unexpected YAML construct."));
        }
    }

    private static void Refuse(AnchorName anchor, TagName tag, ParsingEvent at)
    {
        if (!anchor.IsEmpty)
        {
            throw new FileTreeException(Problem(At(at), FileCodes.AliasRefused, "Anchors and aliases are not allowed."));
        }

        if (!tag.IsEmpty)
        {
            throw new FileTreeException(Problem(At(at), FileCodes.TagRefused, "Tags are not allowed."));
        }
    }

    private void CheckDepth(ParsingEvent start, int depth)
    {
        if (depth > _limits.MaxDepth)
        {
            throw new FileTreeException(Problem(At(start), FileCodes.TooDeep, $"The file is nested deeper than {_limits.MaxDepth} levels."));
        }
    }

    private SequenceNode ReadSequence(Parser parser, SequenceStart start, int depth)
    {
        CheckDepth(start, depth);
        var items = new List<FileNode>();
        while (parser.MoveNext() && parser.Current is { } current and not SequenceEnd)
        {
            if (items.Count >= _limits.MaxSequenceItems)
            {
                throw new FileTreeException(Problem(At(current), FileCodes.TooManyItems, $"A list has more than {_limits.MaxSequenceItems} items."));
            }

            items.Add(ReadNode(parser, current, depth + 1));
        }

        return new SequenceNode(At(start), items);
    }

    private MappingNode ReadMapping(Parser parser, MappingStart start, int depth)
    {
        CheckDepth(start, depth);
        var entries = new List<MappingEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (parser.MoveNext() && parser.Current is { } keyEvent and not MappingEnd)
        {
            if (keyEvent is not Scalar key)
            {
                throw new FileTreeException(Problem(At(keyEvent), keyEvent is AnchorAlias ? FileCodes.AliasRefused : FileCodes.BadKey, "A key must be a plain word."));
            }

            CountNode(key);
            Refuse(key.Anchor, key.Tag, key);
            if (key.Value.Length > _limits.MaxScalarLength)
            {
                throw new FileTreeException(Problem(At(key), FileCodes.ScalarTooLong, $"A key is longer than {_limits.MaxScalarLength} characters."));
            }

            if (key.Style == ScalarStyle.Plain && key.Value == "<<")
            {
                throw new FileTreeException(Problem(At(key), FileCodes.TagRefused, "Merge keys (<<) are not allowed."));
            }

            if (!seen.Add(key.Value))
            {
                throw new FileTreeException(Problem(At(key), FileCodes.DuplicateKey, $"The key '{key.Value}' appears twice in the same mapping."));
            }

            if (seen.Count > _limits.MaxMappingKeys)
            {
                throw new FileTreeException(Problem(At(key), FileCodes.TooManyKeys, $"A mapping has more than {_limits.MaxMappingKeys} keys."));
            }

            if (!parser.MoveNext())
            {
                throw new FileTreeException(Problem(At(key), FileCodes.YamlSyntax, "A key has no value."));
            }

            entries.Add(new MappingEntry(key.Value, At(key), ReadNode(parser, parser.Current!, depth + 1)));
        }

        return new MappingNode(At(start), entries);
    }
}

/// <summary>Carries the one problem that ends a read (a limit or a refused construct) out of the recursive descent.</summary>
internal sealed class FileTreeException(FileProblem problem) : Exception(problem.Message)
{
    public FileProblem Problem { get; } = problem;
}
