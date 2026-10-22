namespace XDrishti.Strategies.File;

/// <summary>The stable problem codes of the reader (pinned by a test).</summary>
public static class FileCodes
{
    /// <summary>The text is not YAML.</summary>
    public const string YamlSyntax = "file.yaml_syntax";

    /// <summary>The text is empty or has no document.</summary>
    public const string Empty = "file.empty";

    /// <summary>More bytes than <see cref="FileLimits.MaxBytes"/>.</summary>
    public const string TooLarge = "file.too_large";

    /// <summary>Nesting deeper than <see cref="FileLimits.MaxDepth"/>.</summary>
    public const string TooDeep = "file.too_deep";

    /// <summary>More nodes than <see cref="FileLimits.MaxNodes"/>.</summary>
    public const string TooManyNodes = "file.too_many_nodes";

    /// <summary>A scalar longer than <see cref="FileLimits.MaxScalarLength"/>.</summary>
    public const string ScalarTooLong = "file.scalar_too_long";

    /// <summary>A mapping with more keys than <see cref="FileLimits.MaxMappingKeys"/>.</summary>
    public const string TooManyKeys = "file.too_many_keys";

    /// <summary>A list with more items than its limit.</summary>
    public const string TooManyItems = "file.too_many_items";

    /// <summary>An anchor or an alias.</summary>
    public const string AliasRefused = "file.alias_refused";

    /// <summary>A tag or a merge key.</summary>
    public const string TagRefused = "file.tag_refused";

    /// <summary>More than one document.</summary>
    public const string MultipleDocuments = "file.multiple_documents";

    /// <summary>The same key twice in one mapping.</summary>
    public const string DuplicateKey = "file.duplicate_key";

    /// <summary>A control character other than tab and line breaks.</summary>
    public const string BadCharacter = "file.bad_character";

    /// <summary>A key that is not a plain word (a mapping or list as a key).</summary>
    public const string BadKey = "file.bad_key";

    /// <summary><c>schema</c> is not 1.</summary>
    public const string UnsupportedSchema = "file.unsupported_schema";

    /// <summary>A key the schema does not know.</summary>
    public const string UnknownField = "file.unknown_field";

    /// <summary>A required key is missing.</summary>
    public const string MissingField = "file.missing_field";

    /// <summary>A mapping where a scalar is expected, or the reverse, or a quoted number.</summary>
    public const string WrongType = "file.wrong_type";

    /// <summary>A scalar that is not a plain decimal number.</summary>
    public const string BadNumber = "file.bad_number";

    /// <summary>A value outside the closed set or range of its field.</summary>
    public const string BadValue = "file.bad_value";

    /// <summary>A time that is not <c>HH:mm</c>.</summary>
    public const string BadTime = "file.bad_time";

    /// <summary>A condition list with both <c>all</c> and <c>any</c>, or neither.</summary>
    public const string BadConditionList = "file.bad_condition_list";

    /// <summary>A warning: <c>test</c> or <c>universe</c> is read but not checked here.</summary>
    public const string SectionNotValidated = "file.section_not_validated";
}
