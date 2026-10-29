using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace QuizBank.Validation;

/// <summary>A schema error at a JSON pointer in the instance.</summary>
public sealed record SchemaError(string Pointer, string Keyword, string Message);

/// <summary>The JSON Schemas in schema/, built together so their relative $refs resolve.</summary>
public sealed partial class BankSchemas
{
    private static readonly Uri SchemaBase = new("https://quizbank.invalid/schema/");

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
        IncludeApplicatorErrors = false,
    };

    // Failing subschemas under these keywords are how the keywords decide, not errors in the document.
    // When the keyword itself fails, it reports on the parent node.
    private static readonly HashSet<string> BranchKeywords = ["if", "contains", "anyOf", "oneOf", "not"];

    // Rules whose generic message does not say what to fix.
    private static readonly Dictionary<(string Pointer, string Keyword), string> Explained = new()
    {
        [("/sources", "contains")] = "Needs at least one source with kind: proof.",
    };

    static BankSchemas()
    {
        ErrorMessages.Pattern = "Value does not match the pattern [[pattern]]";
        ErrorMessages.Enum = "Value [[received]] is not one of [[values]]";
        ErrorMessages.Const = "Value must be [[value]]";
        ErrorMessages.FalseSchema = "Property is not allowed here";
    }

    private BankSchemas(JsonSchema question, JsonSchema concepts, JsonSchema roadmap)
    {
        Question = question;
        Concepts = concepts;
        Roadmap = roadmap;
    }

    public JsonSchema Question { get; }
    public JsonSchema Concepts { get; }
    public JsonSchema Roadmap { get; }

    public static BankSchemas Load(string schemaDirectory)
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };

        // With file base URIs, relative $refs such as "common.schema.json#/$defs/area" silently resolve to
        // nothing and accept any value. A synthetic https base keeps them resolvable on every OS.
        JsonSchema Build(string name) =>
            JsonSchema.FromText(File.ReadAllText(Path.Combine(schemaDirectory, name)), options, new Uri(SchemaBase, name));

        // Shared definitions first: the others $ref into it.
        Build("common.schema.json");
        return new BankSchemas(Build("question.schema.json"), Build("concept.schema.json"), Build("roadmap.schema.json"));
    }

    /// <summary>Schema errors in the order the evaluator reports them.</summary>
    public static IReadOnlyList<SchemaError> Evaluate(JsonSchema schema, JsonNode instance)
    {
        var results = schema.Evaluate(JsonSerializer.SerializeToElement(instance), EvaluationOptions);
        if (results.IsValid) return [];

        IEnumerable<EvaluationResults> nodes = [results, .. results.Details ?? []];
        return nodes
            .Where(node => node.Errors is { Count: > 0 } && !IsInsideBranch(node))
            .SelectMany(node => node.Errors!.Select(error =>
            {
                var pointer = node.InstanceLocation.ToString();
                var message = Explained.GetValueOrDefault((pointer, error.Key)) ?? Unescape(error.Value);
                return new SchemaError(pointer, error.Key, message);
            }))
            .Distinct()
            .ToList();
    }

    private static bool IsInsideBranch(EvaluationResults node)
    {
        var segments = node.EvaluationPath.ToString().Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            if (BranchKeywords.Contains(segments[i]) && segments[i - 1] is not ("properties" or "$defs"))
                return true;
        }

        return false;
    }

    // Messages embed JSON-encoded values such as "EF Core & PostgreSQL".
    private static string Unescape(string message) =>
        UnicodeEscape().Replace(message, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex UnicodeEscape();
}
