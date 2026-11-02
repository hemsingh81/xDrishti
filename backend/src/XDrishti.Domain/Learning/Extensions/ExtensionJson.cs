using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Writes the JSON texts of a stored evaluation (P13-05). A value that is unavailable is written as the string <c>"unavailable"</c>, never as a number (P8-00 D3). Numbers use the shortest round-trip form, so the text is the same on every run.</summary>
internal sealed class ExtensionJson : IDisposable
{
    public const string Unavailable = "unavailable";

    private readonly MemoryStream _stream = new();
    private readonly Utf8JsonWriter _writer;

    public ExtensionJson() => _writer = new Utf8JsonWriter(_stream);

    public ExtensionJson Object(string? name = null)
    {
        Name(name);
        _writer.WriteStartObject();
        return this;
    }

    public ExtensionJson EndObject()
    {
        _writer.WriteEndObject();
        return this;
    }

    public ExtensionJson Array(string? name = null)
    {
        Name(name);
        _writer.WriteStartArray();
        return this;
    }

    public ExtensionJson EndArray()
    {
        _writer.WriteEndArray();
        return this;
    }

    public ExtensionJson Text(string name, string? value)
    {
        _writer.WriteString(name, value ?? Unavailable);
        return this;
    }

    public ExtensionJson Item(string value)
    {
        _writer.WriteStringValue(value);
        return this;
    }

    public ExtensionJson Count(string name, long value)
    {
        _writer.WriteNumber(name, value);
        return this;
    }

    public ExtensionJson Unsigned(string name, ulong value)
    {
        _writer.WriteNumber(name, value);
        return this;
    }

    public ExtensionJson Flag(string name, bool value)
    {
        _writer.WriteBoolean(name, value);
        return this;
    }

    public ExtensionJson Number(string name, double? value)
    {
        if (value is { } v && double.IsFinite(v))
        {
            _writer.WriteNumber(name, v);
        }
        else
        {
            _writer.WriteString(name, Unavailable);
        }

        return this;
    }

    public ExtensionJson Date(string name, DateOnly value)
    {
        _writer.WriteString(name, value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return this;
    }

    public string Finish()
    {
        _writer.Flush();
        return Encoding.UTF8.GetString(_stream.ToArray());
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }

    private void Name(string? name)
    {
        if (name is not null)
        {
            _writer.WritePropertyName(name);
        }
    }
}
