namespace XDrishti.Strategies.File;

/// <summary>Where a node is in the file: 1-based line and column of its first and one past its last character.</summary>
public readonly record struct FilePosition(int Line, int Column, int EndLine, int EndColumn);

/// <summary>A node of the generic tree the reader builds from the YAML events (P3-02c D3): a mapping, a sequence or a scalar, each with its position. The library's object deserializer is never used.</summary>
internal abstract record FileNode(FilePosition Position);

/// <summary>A scalar: its raw text, whether it was written in quotes, and whether it is a block scalar (<c>|</c> or <c>&gt;</c>). Nothing is typed: the field that reads it converts it.</summary>
internal sealed record ScalarNode(FilePosition Position, string Text, bool Quoted, bool Block) : FileNode(Position);

/// <summary>A sequence.</summary>
internal sealed record SequenceNode(FilePosition Position, IReadOnlyList<FileNode> Items) : FileNode(Position);

/// <summary>A mapping, keys in file order, each key with its own position.</summary>
internal sealed record MappingNode(FilePosition Position, IReadOnlyList<MappingEntry> Entries) : FileNode(Position)
{
    /// <summary>The value of a key, or null.</summary>
    public FileNode? Get(string key) => Entries.FirstOrDefault(e => e.Key == key)?.Value;
}

/// <summary>One key and its value.</summary>
internal sealed record MappingEntry(string Key, FilePosition KeyPosition, FileNode Value);
