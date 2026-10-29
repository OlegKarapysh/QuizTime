using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuizBank.Model;

/// <summary>
/// Serializer settings matching the YAML files: snake_case keys, kebab-case enum values.
/// Only schema-valid documents are deserialized, so required fields are enforced by the schema.
/// </summary>
public static class BankJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
        RespectNullableAnnotations = true,
    };
}

/// <summary>Reads either a single string or a list of strings as a list.</summary>
public sealed class OneOrManyConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? [reader.GetString()!]
            : JsonSerializer.Deserialize<string[]>(ref reader, options)!;

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        if (value.Count == 1) writer.WriteStringValue(value[0]);
        else JsonSerializer.Serialize(writer, value, options);
    }
}
