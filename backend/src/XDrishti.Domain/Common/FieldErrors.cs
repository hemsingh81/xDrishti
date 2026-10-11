namespace XDrishti.Domain.Common;

/// <summary>Collects the rejected fields of one request so the caller gets all problems at once.</summary>
public sealed class FieldErrors
{
    private readonly List<FieldError> _items = [];

    public bool HasErrors => _items.Count > 0;

    public IReadOnlyList<FieldError> Items => _items;

    public void Add(string field, string message) => _items.Add(new FieldError(field, message));

    /// <summary>Adds all errors of another collector (for example the sub-object of a request), prefixing their field names.</summary>
    public void AddRange(FieldErrors other, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var item in other._items)
        {
            _items.Add(prefix.Length == 0 ? item : item with { Field = prefix + "." + item.Field });
        }
    }

    public Error ToError(string code, string message) => Error.Validation(code, message) with { Fields = _items.ToArray() };
}
