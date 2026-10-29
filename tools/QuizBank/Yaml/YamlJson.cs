using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace QuizBank.Yaml;

/// <summary>1-based line and column in a source file.</summary>
public readonly record struct SourcePosition(int Line, int Column)
{
    /// <summary>The position of the character at <paramref name="offset"/> in <paramref name="text"/>.</summary>
    public static SourcePosition FromOffset(string text, int offset)
    {
        var lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        return new SourcePosition(1 + text.AsSpan(0, offset).Count('\n'), offset - lineStart + 1);
    }
}

/// <summary>A YAML file that could not be read as a single JSON-compatible document.</summary>
public sealed class YamlLoadException(string message, SourcePosition position) : Exception(message)
{
    public SourcePosition Position { get; } = position;
}

/// <summary>A YAML document converted to JSON, with the source position of every value keyed by JSON pointer.</summary>
public sealed class YamlJsonDocument(JsonNode root, IReadOnlyDictionary<string, SourcePosition> positions)
{
    public JsonNode Root { get; } = root;

    /// <summary>Position of the value at <paramref name="pointer"/>, or of its nearest existing ancestor.</summary>
    public SourcePosition PositionOf(string pointer)
    {
        while (true)
        {
            if (positions.TryGetValue(pointer, out var position)) return position;
            if (pointer.Length == 0) return new SourcePosition(1, 1);
            pointer = pointer[..pointer.LastIndexOf('/')];
        }
    }
}

/// <summary>
/// Reads YAML into <see cref="JsonNode"/>s. Plain scalars resolve as in the YAML 1.2 core schema
/// (null, booleans, decimal numbers); everything else, including dates, stays a string.
/// </summary>
public static partial class YamlJson
{
    public static YamlJsonDocument Load(string path) => Parse(File.ReadAllText(path));

    public static YamlJsonDocument Parse(string text)
    {
        var stream = new YamlStream();
        var parser = new Parser(new StringReader(text));
        try
        {
            stream.Load(parser);
        }
        catch (YamlException e)
        {
            throw new YamlLoadException(StripMarks(e.InnerException?.Message ?? e.Message), Position(e.Start));
        }
        catch (InvalidOperationException)
        {
            // YamlDotNet's parser fails this way on some unclosed flow collections, e.g. "a: [1, 2" at the end of a file.
            var position = parser.Current is { } current ? Position(current.Start) : new SourcePosition(1, 1);
            throw new YamlLoadException("Invalid YAML: look for an unclosed [ or { from here on.", position);
        }

        if (stream.Documents.Count == 0) throw new YamlLoadException("File is empty.", new SourcePosition(1, 1));
        if (stream.Documents.Count > 1)
            throw new YamlLoadException("File holds more than one YAML document.", Position(stream.Documents[1].RootNode.Start));

        var positions = new Dictionary<string, SourcePosition>();
        var rootNode = stream.Documents[0].RootNode;
        var root = Convert(rootNode, "", positions)
                   ?? throw new YamlLoadException("File is empty.", Position(rootNode.Start));
        return new YamlJsonDocument(root, positions);
    }

    private static JsonNode? Convert(YamlNode node, string pointer, Dictionary<string, SourcePosition> positions)
    {
        positions[pointer] = Position(node.Start);
        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    // YamlDotNet already rejects duplicate keys while loading.
                    if (keyNode is not YamlScalarNode { Value: { } key })
                        throw new YamlLoadException("Mapping keys must be plain strings.", Position(keyNode.Start));
                    obj[key] = Convert(valueNode, $"{pointer}/{EscapePointer(key)}", positions);
                }
                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                for (var i = 0; i < sequence.Children.Count; i++)
                    array.Add(Convert(sequence.Children[i], $"{pointer}/{i}", positions));
                return array;

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                throw new YamlLoadException($"Unsupported YAML node '{node.NodeType}'.", Position(node.Start));
        }
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        var tag = scalar.Tag.IsEmpty || scalar.Tag.IsNonSpecific ? null : scalar.Tag.Value;

        if (tag == "tag:yaml.org,2002:str") return JsonValue.Create(value);
        if (tag is null && scalar.Style != ScalarStyle.Plain) return JsonValue.Create(value);

        return value switch
        {
            "" or "~" or "null" or "Null" or "NULL" => null,
            "true" or "True" or "TRUE" => JsonValue.Create(true),
            "false" or "False" or "FALSE" => JsonValue.Create(false),
            _ when IntPattern().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
                => JsonValue.Create(l),
            _ when FloatPattern().IsMatch(value)
                => JsonValue.Create(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value),
        };
    }

    private static SourcePosition Position(Mark mark) => new((int)mark.Line, (int)mark.Column);

    private static string EscapePointer(string key) => key.Replace("~", "~0").Replace("/", "~1");

    private static string StripMarks(string message) => MarkPrefix().Replace(message, "");

    [GeneratedRegex(@"^[-+]?[0-9]+$")]
    private static partial Regex IntPattern();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")]
    private static partial Regex FloatPattern();

    // YamlDotNet prefixes messages with "(Line: 3, Col: 1, Idx: 20) - (Line: 3, Col: 5, Idx: 24): ".
    [GeneratedRegex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ")]
    private static partial Regex MarkPrefix();
}
