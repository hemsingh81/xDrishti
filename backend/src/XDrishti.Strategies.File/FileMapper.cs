using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File;

/// <summary>
/// Maps the generic tree to the <see cref="StrategyDefinition"/> of schema 1 (P3-02c D1, D4, D5). Table-driven by <see cref="FileShape"/>; never throws on user input; reports every problem of the file in one pass, in file order.
/// Nothing is typed implicitly: every scalar is text and the field that reads it converts it by its own rule. It judges the shape only: the rules of the language and the relations between fields are the compile's.
/// </summary>
internal sealed class FileMapper
{
    private readonly List<FileProblem> _problems;
    private readonly Dictionary<string, FilePosition> _positions = new(StringComparer.Ordinal);

    private FileMapper(List<FileProblem> problems) => _problems = problems;

    /// <summary>The definition (null when there is an error), the version of the envelope and the positions by model path.</summary>
    public static (StrategyDefinition? Definition, int? Version, IReadOnlyDictionary<string, FilePosition> Positions) Map(FileNode root, List<FileProblem> problems)
    {
        var mapper = new FileMapper(problems);
        var (definition, version) = mapper.Root(root);
        var failed = problems.Any(p => p.Severity == ProblemSeverity.Error);
        return (failed ? null : definition, version, mapper._positions);
    }

    private static string Join(string parent, string key) => parent.Length == 0 ? key : parent + "." + key;

    private void Add(string path, FilePosition at, string code, string message, ProblemSeverity severity = ProblemSeverity.Error) =>
        _problems.Add(new FileProblem(path, at.Line, at.Column, at.EndLine, at.EndColumn, code, severity, message));

    private void Mark(string path, FileNode node) => _positions[path] = node.Position;

    private (StrategyDefinition? Definition, int? Version) Root(FileNode root)
    {
        if (Mapping(root, string.Empty, "The file") is not { } map)
        {
            return (null, null);
        }

        CheckKeys(map, string.Empty, FileShape.Root);
        WarnOpaque(map, string.Empty);
        if (map.Get("strategy") is not { } strategy)
        {
            Add(string.Empty, map.Position, FileCodes.MissingField, "The file needs a 'strategy:' section.");
            return (null, null);
        }

        return Strategy(strategy);
    }

    private void WarnOpaque(MappingNode map, string path)
    {
        foreach (var entry in map.Entries.Where(e => FileShape.Opaque.Contains(e.Key)))
        {
            Add(Join(path, entry.Key), entry.KeyPosition, FileCodes.SectionNotValidated, $"'{entry.Key}' is read but not checked here; it says how to test a strategy, not what it is.", ProblemSeverity.Warning);
        }
    }

    private (StrategyDefinition? Definition, int? Version) Strategy(FileNode node)
    {
        if (Mapping(node, "strategy", "'strategy'") is not { } map)
        {
            return (null, null);
        }

        Mark(string.Empty, map);
        CheckKeys(map, string.Empty, [.. FileShape.Header, .. FileShape.Side, FileShape.SellKey, .. FileShape.Trailer, .. FileShape.Opaque]);
        WarnOpaque(map, string.Empty);

        var schema = StrategyDefinition.CurrentSchema;
        if (map.Get("schema") is { } schemaNode && Number(schemaNode, "schema") is { } declared && declared != StrategyDefinition.CurrentSchema)
        {
            Add("schema", schemaNode.Position, FileCodes.UnsupportedSchema, $"Schema {declared.ToString(CultureInfo.InvariantCulture)} is not supported; this reader reads schema 1.");
        }

        var id = Required(map, "id") is { } idNode ? Text(idNode, "id") : null;
        var style = Required(map, "style") is { } styleNode ? Choice(StrategyNames.Styles, styleNode, "style") : null;
        var version = map.Get("version") is { } versionNode ? WholeNumber(versionNode, "version", 1) : null;
        List<Timeframe>? timeframes = map.Get("timeframes") is { } tfNode ? StructList(tfNode, "timeframes", ReadTimeframe) : [];
        List<Side>? sides = map.Get("sides") is { } sidesNode ? StructList(sidesNode, "sides", (n, p) => Choice(StrategyNames.Sides, n, p)) : [];
        List<StrategyParam>? parameters = map.Get("params") is { } paramsNode ? Params(paramsNode) : [];
        var buy = FileShape.Side.Any(k => map.Get(k) is not null) ? ReadSide(map, "buy") : null;
        var sell = map.Get(FileShape.SellKey) is { } sellNode && Mapping(sellNode, "sell", "'sell'") is { } sellMap ? SellSide(sellMap) : null;
        var rules = map.Get("time_rules") is { } rulesNode ? ReadTimeRules(rulesNode) : null;
        List<string>? filters = map.Get("filters") is { } filtersNode ? TextList(filtersNode, "filters") : [];

        if (id is null || style is null || timeframes is null || sides is null || parameters is null || filters is null || schema != StrategyDefinition.CurrentSchema)
        {
            return (null, version);
        }

        return (new StrategyDefinition(schema, id, style.Value, timeframes, sides, parameters, buy, sell, rules, filters), version);
    }

    private SideRules? SellSide(MappingNode map)
    {
        Mark("sell", map);
        CheckKeys(map, "sell", FileShape.Side);
        return ReadSide(map, "sell");
    }

    private SideRules ReadSide(MappingNode map, string path)
    {
        var setup = map.Get("setup") is { } setupNode ? Conditions(setupNode, Join(path, "setup")) : null;
        var trigger = map.Get("trigger") is { } triggerNode ? Conditions(triggerNode, Join(path, "trigger")) : null;
        var invalidate = map.Get("invalidate") is { } invalidateNode ? Conditions(invalidateNode, Join(path, "invalidate")) : null;
        var entry = map.Get("entry") is { } entryNode ? Entry(entryNode, Join(path, "entry")) : null;
        var stop = map.Get("stop") is { } stopNode ? Stop(stopNode, Join(path, "stop")) : null;
        var exits = map.Get("exits") is { } exitsNode ? Flat<ExitPolicy, ExitSpec>(exitsNode, Join(path, "exits"), "policy", (n, p) => Choice(StrategyNames.Policies, n, p), (k, ps) => new ExitSpec(k, ps), k => ExitParameterInfo.Of(k)) : null;
        var overlays = map.Get("overlays") is { } overlaysNode ? Flat<OverlayKind, OverlaySpec>(overlaysNode, Join(path, "overlays"), "kind", (n, p) => Choice(StrategyNames.Overlays, n, p), (k, ps) => new OverlaySpec(k, ps), k => ExitParameterInfo.Of(k)) : null;
        return new SideRules(setup, trigger, invalidate, entry, stop, exits, overlays);
    }

    private ConditionList? Conditions(FileNode node, string path)
    {
        if (Mapping(node, path, "A condition list") is not { } map)
        {
            return null;
        }

        Mark(path, map);
        CheckKeys(map, path, StrategyNames.Modes.Names);
        var present = StrategyNames.Modes.Names.Where(k => map.Get(k) is not null).ToArray();
        if (present.Length != 1)
        {
            if (present.Length > 1 || map.Entries.Count == 0)
            {
                Add(path, map.Position, FileCodes.BadConditionList, "A condition list has exactly one key, 'all' or 'any'.");
            }

            return null;
        }

        if (!StrategyNames.Modes.TryParse(present[0], out var mode))
        {
            return null;
        }

        var items = TextList(map.Get(present[0])!, path);
        return items is null ? null : new ConditionList(mode, items);
    }

    private EntrySpec? Entry(FileNode node, string path)
    {
        if (Mapping(node, path, "'entry'") is not { } map)
        {
            return null;
        }

        Mark(path, map);
        CheckKeys(map, path, ["type"]);
        return Required(map, "type", path) is { } type && Choice(StrategyNames.Entries, type, Join(path, "type")) is { } kind ? new EntrySpec(kind) : null;
    }

    private StopSpec? Stop(FileNode node, string path)
    {
        if (Mapping(node, path, "'stop'") is not { } map)
        {
            return null;
        }

        Mark(path, map);
        CheckKeys(map, path, ["type", .. FileShape.StopFields]);
        var kind = Required(map, "type", path) is { } type ? Choice(StrategyNames.Stops, type, Join(path, "type")) : null;
        var timeframe = map.Get("timeframe") is { } tfNode ? ReadTimeframe(tfNode, Join(path, "timeframe")) : null;
        var quantities = FileShape.StopFields.Skip(1).ToDictionary(f => f, f => map.Get(f) is { } n ? Qty(n, Join(path, f)) : null);
        return kind is null ? null : new StopSpec(kind.Value, timeframe, quantities["pct"], quantities["mult"], quantities["lookback"], quantities["minutes"], quantities["buffer_atr"], quantities["atr_period"]);
    }

    private List<T>? Flat<TKind, T>(FileNode node, string path, string kindKey, Func<FileNode, string, TKind?> kind, Func<TKind, IReadOnlyList<ExitParameter>, T> make, Func<TKind, IReadOnlyList<ExitParameterInfo>> table)
        where TKind : struct
    {
        var items = Sequence(node, path);
        if (items is null)
        {
            return null;
        }

        var result = new List<T>();
        var ok = true;
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (Mapping(items[i], itemPath, "An item") is not { } map)
            {
                ok = false;
                continue;
            }

            Mark(itemPath, map);
            if (Required(map, kindKey, itemPath) is not { } kindNode || kind(kindNode, Join(itemPath, kindKey)) is not { } chosen)
            {
                ok = false;
                continue;
            }

            var known = table(chosen);
            var parameters = new List<ExitParameter>();
            foreach (var entry in map.Entries.Where(e => e.Key != kindKey))
            {
                var paramPath = Join(itemPath, entry.Key);
                if (known.All(k => k.Name != entry.Key))
                {
                    Add(paramPath, entry.KeyPosition, FileCodes.UnknownField, $"'{entry.Key}' is not a parameter of {Describe(map, kindKey)}. Known: {string.Join(", ", known.Select(k => k.Name))}.");
                    ok = false;
                    continue;
                }

                if (Values(entry.Value, paramPath) is { } values)
                {
                    parameters.Add(new ExitParameter(entry.Key, values));
                }
                else
                {
                    ok = false;
                }
            }

            result.Add(make(chosen, parameters));
        }

        return ok ? result : null;
    }

    private static string Describe(MappingNode map, string kindKey) => map.Get(kindKey) is ScalarNode scalar ? scalar.Text : string.Empty;

    private List<Quantity>? Values(FileNode node, string path)
    {
        Mark(path, node);
        if (node is not SequenceNode sequence)
        {
            return Qty(node, path) is { } one ? [one] : null;
        }

        if (sequence.Items.Count is 0 or > ExitParameterInfo.MaxListValues)
        {
            Add(path, node.Position, FileCodes.TooManyItems, $"A list of values has 1 to {ExitParameterInfo.MaxListValues} items.");
            return null;
        }

        var values = sequence.Items.Select((n, i) => Qty(n, $"{path}[{i}]")).ToList();
        return values.All(v => v is not null) ? [.. values.Select(v => v!.Value)] : null;
    }

    private List<StrategyParam>? Params(FileNode node)
    {
        if (Mapping(node, "params", "'params'") is not { } map)
        {
            return null;
        }

        Mark("params", map);
        var result = new List<StrategyParam>();
        var ok = true;
        foreach (var entry in map.Entries)
        {
            if (Number(entry.Value, Join("params", entry.Key)) is { } value)
            {
                result.Add(new StrategyParam(entry.Key, value));
            }
            else
            {
                ok = false;
            }
        }

        return ok ? result : null;
    }

    private TimeRules? ReadTimeRules(FileNode node)
    {
        if (Mapping(node, "time_rules", "'time_rules'") is not { } map)
        {
            return null;
        }

        Mark("time_rules", map);
        CheckKeys(map, "time_rules", FileShape.TimeFields);
        var ok = true;
        TimeOnly? Time(string key)
        {
            if (map.Get(key) is not { } n)
            {
                return null;
            }

            var path = Join("time_rules", key);
            Mark(path, n);
            if (Text(n, path) is not { } text)
            {
                ok = false;
                return null;
            }

            if (!FileShape.IsTime(text))
            {
                Add(path, n.Position, FileCodes.BadTime, "A time is written HH:mm, for example 14:30.");
                ok = false;
                return null;
            }

            return new TimeOnly(int.Parse(text[..2], CultureInfo.InvariantCulture), int.Parse(text[3..], CultureInfo.InvariantCulture));
        }

        var first = Time("first_entry");
        var last = Time("last_entry");
        var square = Time("square_off");
        int? hold = map.Get("max_hold_sessions") is { } holdNode ? WholeNumber(holdNode, "time_rules.max_hold_sessions", 1) : null;
        ok &= map.Get("max_hold_sessions") is null || hold is not null;
        return ok ? new TimeRules(first, last, square, hold) : null;
    }

    private MappingNode? Mapping(FileNode node, string path, string what)
    {
        if (node is MappingNode map)
        {
            return map;
        }

        Add(path, node.Position, FileCodes.WrongType, $"{what} must be a mapping (key: value pairs).");
        return null;
    }

    private IReadOnlyList<FileNode>? Sequence(FileNode node, string path)
    {
        Mark(path, node);
        if (node is SequenceNode sequence)
        {
            return sequence.Items;
        }

        Add(path, node.Position, FileCodes.WrongType, "This must be a list.");
        return null;
    }

    private List<string>? TextList(FileNode node, string path)
    {
        var items = Sequence(node, path);
        if (items is null)
        {
            return null;
        }

        var result = items.Select((n, i) => Text(n, $"{path}[{i}]")).ToList();
        return result.All(t => t is not null) ? [.. result.Select(t => t!)] : null;
    }

    private List<T>? StructList<T>(FileNode node, string path, Func<FileNode, string, T?> read)
        where T : struct
    {
        var items = Sequence(node, path);
        if (items is null)
        {
            return null;
        }

        var result = items.Select((n, i) => read(n, $"{path}[{i}]")).ToList();
        return result.All(t => t is not null) ? [.. result.Select(t => t!.Value)] : null;
    }

    private void CheckKeys(MappingNode map, string path, IEnumerable<string> allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in map.Entries.Where(e => !set.Contains(e.Key)))
        {
            Add(Join(path, entry.Key), entry.KeyPosition, FileCodes.UnknownField, $"'{entry.Key}' is not a known field here. Known: {string.Join(", ", set)}.");
        }
    }

    private FileNode? Required(MappingNode map, string key, string path = "")
    {
        var node = map.Get(key);
        if (node is null)
        {
            Add(Join(path, key), map.Position, FileCodes.MissingField, $"'{key}' is required.");
        }

        return node;
    }

    private string? Text(FileNode node, string path)
    {
        Mark(path, node);
        if (node is ScalarNode scalar)
        {
            return scalar.Text;
        }

        Add(path, node.Position, FileCodes.WrongType, "This must be a single value.");
        return null;
    }

    private T? Choice<T>(NameTable<T> table, FileNode node, string path)
        where T : struct, Enum
    {
        if (Text(node, path) is not { } text)
        {
            return null;
        }

        if (table.TryParse(text, out var value))
        {
            return value;
        }

        Add(path, node.Position, FileCodes.BadValue, $"'{text}' is not one of: {string.Join(", ", table.Names)}.");
        return null;
    }

    private Timeframe? ReadTimeframe(FileNode node, string path)
    {
        if (Text(node, path) is not { } text)
        {
            return null;
        }

        if (Timeframes.TryParse(text, out var frame))
        {
            return frame;
        }

        Add(path, node.Position, FileCodes.BadValue, $"'{text}' is not a timeframe. Use one of: {string.Join(", ", FileShape.TimeframeCodes)}.");
        return null;
    }

    private double? Number(FileNode node, string path)
    {
        Mark(path, node);
        if (node is not ScalarNode scalar || scalar.Quoted)
        {
            Add(path, node.Position, FileCodes.WrongType, "This must be a number written without quotes.");
            return null;
        }

        if (FileShape.IsNumber(scalar.Text) && double.Parse(scalar.Text, NumberStyles.Float, CultureInfo.InvariantCulture) is var value && double.IsFinite(value))
        {
            return value;
        }

        Add(path, node.Position, FileCodes.BadNumber, $"'{scalar.Text}' is not a plain decimal number (digits, an optional fraction and exponent; no +, no underscores, no hex).");
        return null;
    }

    private int? WholeNumber(FileNode node, string path, int min)
    {
        if (Number(node, path) is not { } value)
        {
            return null;
        }

        if (value < min || value > 100_000 || value != Math.Floor(value))
        {
            Add(path, node.Position, FileCodes.BadValue, $"This must be a whole number from {min} to 100000.");
            return null;
        }

        return (int)value;
    }

    private Quantity? Qty(FileNode node, string path)
    {
        Mark(path, node);
        if (node is not ScalarNode scalar || scalar.Quoted)
        {
            Add(path, node.Position, FileCodes.WrongType, "This must be a number or the name of a parameter, written without quotes.");
            return null;
        }

        if (FileShape.IsParamName(scalar.Text))
        {
            return Quantity.Named(scalar.Text);
        }

        return Number(node, path) is { } number ? Quantity.Of(number) : null;
    }
}
