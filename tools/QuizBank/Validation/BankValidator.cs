using System.Text.Json;
using Json.Schema;
using QuizBank.Model;
using QuizBank.Yaml;

namespace QuizBank.Validation;

public sealed record ValidationReport(IReadOnlyList<Diagnostic> Errors, int Roadmaps, int Nodes, int Concepts, int Questions)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Gate 1 structure checks: every roadmap, concept and question file against its schema, then against each other
/// (unique ids, file paths, nodes, concepts, answer ids, check files, blind-solve records).
/// </summary>
public sealed class BankValidator
{
    private readonly string _root;
    private readonly List<Diagnostic> _errors = [];

    // Whether every roadmap and concept file loaded. Reference checks against an incomplete set
    // would only repeat the load errors, so they are skipped until those are fixed.
    private bool _roadmapsComplete;
    private bool _conceptsComplete;

    private BankValidator(string root) => _root = root;

    public static ValidationReport Validate(string root) => new BankValidator(Path.GetFullPath(root)).Run();

    private ValidationReport Run()
    {
        var schemas = BankSchemas.Load(Path.Combine(_root, "schema"));
        var roadmaps = LoadAll<Roadmap>("roadmaps", schemas.Roadmap, SearchOption.TopDirectoryOnly, out _roadmapsComplete);
        var conceptFiles = LoadAll<List<Concept>>("concepts", schemas.Concepts, SearchOption.TopDirectoryOnly, out _conceptsComplete);
        var questions = LoadAll<Question>("questions", schemas.Question, SearchOption.AllDirectories, out _);

        var nodesByRoadmap = CheckRoadmaps(roadmaps);
        var concepts = CheckConcepts(conceptFiles, nodesByRoadmap);
        CheckQuestions(questions, nodesByRoadmap, concepts);
        CheckForOrphanChecks();

        // Files in the order their first error was found (roadmaps, concepts, questions, checks), so root causes come first.
        var fileOrder = _errors.Select(e => e.File).Distinct().Index().ToDictionary(x => x.Item, x => x.Index);
        var errors = _errors
            .OrderBy(e => fileOrder[e.File])
            .ThenBy(e => e.Line ?? 0)
            .ThenBy(e => e.Column ?? 0)
            .ToList();
        return new ValidationReport(errors, roadmaps.Count, nodesByRoadmap.Values.Sum(n => n.Count), concepts.Count,
            QuestionFiles().Count());
    }

    private sealed record Loaded<T>(string Path, YamlJsonDocument Document, T Value);

    private sealed record ConceptEntry(Concept Concept, string File);

    /// <summary>Parses and schema-checks every YAML file under a folder. Files with errors are reported and skipped.</summary>
    private List<Loaded<T>> LoadAll<T>(string folder, JsonSchema schema, SearchOption search, out bool complete)
    {
        var errorsBefore = _errors.Count;
        var loaded = new List<Loaded<T>>();
        complete = true;

        var directory = Path.Combine(_root, folder);
        if (!Directory.Exists(directory)) return loaded;

        foreach (var file in Directory.EnumerateFiles(directory, "*.y*ml", search).Order(StringComparer.Ordinal))
        {
            var path = Relative(file);
            if (file.EndsWith(".yml", StringComparison.Ordinal))
            {
                Error(path, "Use the .yaml extension.");
                continue;
            }

            YamlJsonDocument document;
            try
            {
                document = YamlJson.Load(file);
            }
            catch (YamlLoadException e)
            {
                _errors.Add(new Diagnostic(path, e.Position.Line, e.Position.Column, e.Message));
                continue;
            }

            var schemaErrors = BankSchemas.Evaluate(schema, document.Root);
            foreach (var error in schemaErrors)
            {
                var position = document.PositionOf(error.Pointer);
                _errors.Add(new Diagnostic(path, position.Line, position.Column,
                    error.Pointer.Length == 0 ? error.Message : $"{error.Pointer}: {error.Message}"));
            }

            if (schemaErrors.Count == 0)
                loaded.Add(new Loaded<T>(path, document, document.Root.Deserialize<T>(BankJson.Options)!));
        }

        complete = _errors.Count == errorsBefore;
        return loaded;
    }

    private Dictionary<string, HashSet<string>> CheckRoadmaps(List<Loaded<Roadmap>> roadmaps)
    {
        var nodesByRoadmap = new Dictionary<string, HashSet<string>>();
        foreach (var item in roadmaps)
        {
            var roadmap = item.Value;
            var fileId = Path.GetFileNameWithoutExtension(item.Path);
            if (roadmap.Id != fileId)
                Error(item, "/id", $"Roadmap id '{roadmap.Id}' must match the file name '{fileId}'.");

            var ids = new HashSet<string>();
            for (var i = 0; i < roadmap.Nodes.Count; i++)
            {
                if (!ids.Add(roadmap.Nodes[i].Id))
                    Error(item, $"/nodes/{i}/id", $"Duplicate node id '{roadmap.Nodes[i].Id}'.");
            }

            var parentOf = roadmap.Nodes.DistinctBy(n => n.Id).ToDictionary(n => n.Id, n => n.Parent);
            for (var i = 0; i < roadmap.Nodes.Count; i++)
            {
                var node = roadmap.Nodes[i];
                if (node.Parent is null) continue;
                if (!ids.Contains(node.Parent))
                {
                    Error(item, $"/nodes/{i}/parent", $"Parent '{node.Parent}' is not a node in this roadmap.");
                    continue;
                }

                var seen = new HashSet<string> { node.Id };
                for (var parent = node.Parent; parent is not null; parent = parentOf.GetValueOrDefault(parent))
                {
                    if (seen.Add(parent)) continue;
                    Error(item, $"/nodes/{i}/parent", $"The parent chain of '{node.Id}' loops back to '{parent}'.");
                    break;
                }
            }

            if (roadmap.Source is { } source && roadmap.Nodes.Count > source.TotalNodes)
                Error(item, "/source/total_nodes",
                    $"The mirror holds {roadmap.Nodes.Count} nodes, more than total_nodes ({source.TotalNodes}).");

            if (!nodesByRoadmap.TryAdd(roadmap.Id, ids))
                Error(item, "/id", $"Duplicate roadmap id '{roadmap.Id}'.");
        }

        return nodesByRoadmap;
    }

    private Dictionary<string, ConceptEntry> CheckConcepts(
        List<Loaded<List<Concept>>> conceptFiles, Dictionary<string, HashSet<string>> nodesByRoadmap)
    {
        var allNodes = nodesByRoadmap.Values.SelectMany(nodes => nodes).ToHashSet();
        var concepts = new Dictionary<string, ConceptEntry>();
        foreach (var item in conceptFiles)
        {
            for (var i = 0; i < item.Value.Count; i++)
            {
                var concept = item.Value[i];
                if (concepts.TryGetValue(concept.Id, out var first))
                {
                    Error(item, $"/{i}/id", $"Duplicate concept id '{concept.Id}' (also in {first.File}).");
                    continue;
                }

                concepts.Add(concept.Id, new ConceptEntry(concept, item.Path));
                for (var j = 0; j < concept.Nodes.Count; j++)
                {
                    if (_roadmapsComplete && !allNodes.Contains(concept.Nodes[j]))
                        Error(item, $"/{i}/nodes/{j}", $"Node '{concept.Nodes[j]}' is not in any roadmap.");
                }
            }
        }

        return concepts;
    }

    private void CheckQuestions(
        List<Loaded<Question>> questions,
        Dictionary<string, HashSet<string>> nodesByRoadmap,
        Dictionary<string, ConceptEntry> concepts)
    {
        var pathsById = new Dictionary<string, string>();
        foreach (var item in questions)
        {
            var question = item.Value;

            var expectedPath = $"questions/{question.Roadmap}/{question.Node}/{question.Id}.yaml";
            if (item.Path != expectedPath)
                Error(item, "", $"File must be at {expectedPath} to match its roadmap, node and id.");

            if (!pathsById.TryAdd(question.Id, item.Path))
                Error(item, "/id", $"Duplicate question id '{question.Id}' (also in {pathsById[question.Id]}).");
            if (question.Id[..^4] != question.Node)
                Error(item, "/id", $"Id must be '{question.Node}-NNN' to match its node.");

            if (_roadmapsComplete)
            {
                if (!nodesByRoadmap.TryGetValue(question.Roadmap, out var nodes))
                    Error(item, "/roadmap", $"Roadmap '{question.Roadmap}' does not exist in roadmaps/.");
                else if (!nodes.Contains(question.Node))
                    Error(item, "/node", $"Node '{question.Node}' is not in roadmaps/{question.Roadmap}.yaml.");
            }

            if (!concepts.TryGetValue(question.Concept, out var entry))
            {
                if (_conceptsComplete)
                    Error(item, "/concept", $"Concept '{question.Concept}' is not in the concept registry.");
            }
            else
            {
                if (entry.Concept.Area != question.Area)
                    Error(item, "/area",
                        $"Area '{question.Area}' differs from the area of concept '{question.Concept}' ('{entry.Concept.Area}').");
                if (!entry.Concept.Nodes.Contains(question.Node))
                    Error(item, "/node",
                        $"Concept '{question.Concept}' does not list node '{question.Node}'; add it to the concept in {entry.File}.");
            }

            var optionIds = CheckOptionsAndAnswer(item);
            CheckVerification(item, optionIds);
        }
    }

    private HashSet<string> CheckOptionsAndAnswer(Loaded<Question> item)
    {
        var question = item.Value;
        var optionIds = new HashSet<string>();
        for (var i = 0; i < question.Options.Count; i++)
        {
            if (!optionIds.Add(question.Options[i].Id))
                Error(item, $"/options/{i}/id", $"Duplicate option id '{question.Options[i].Id}'.");
        }

        for (var i = 0; i < question.Answer.Count; i++)
        {
            if (!optionIds.Contains(question.Answer[i]))
                Error(item, $"/answer/{i}", $"Answer '{question.Answer[i]}' is not an option id.");
        }

        if (question.Type == QuestionType.Ordering &&
            (question.Answer.Count != optionIds.Count || !optionIds.SetEquals(question.Answer)))
            Error(item, "/answer", "An ordering answer must list every option id exactly once.");

        return optionIds;
    }

    private void CheckVerification(Loaded<Question> item, HashSet<string> optionIds)
    {
        var question = item.Value;
        if (question.Verification is not { } verification) return;

        var extension = verification.Method switch
        {
            VerificationMethod.Run => "cs",
            VerificationMethod.Sql => "sql",
            _ => null,
        };
        if (extension is not null)
        {
            var expected = $"checks/{question.Id}/check.{extension}";
            if (verification.Check != expected)
                Error(item, "/verification/check", $"The check for method '{verification.Method.ToString().ToLowerInvariant()}' must be at {expected}.");
            else if (!File.Exists(Path.Combine(_root, expected)))
                Error(item, "/verification/check", $"{expected} does not exist.");
        }

        if (verification.BlindSolve is not { } solve) return;

        for (var i = 0; i < solve.Picked.Count; i++)
        {
            if (!optionIds.Contains(solve.Picked[i]))
                Error(item, $"/verification/blind_solve/picked/{i}", $"Blind-solve pick '{solve.Picked[i]}' is not an option id.");
        }

        var matches = question.Type == QuestionType.Ordering
            ? solve.Picked.SequenceEqual(question.Answer)
            : solve.Picked.ToHashSet().SetEquals(question.Answer);
        if (solve.Agreed != matches)
            Error(item, "/verification/blind_solve/agreed", matches
                ? "The blind-solve pick matches the answer, so agreed must be true."
                : $"The blind-solve pick ({string.Join(", ", solve.Picked)}) differs from the answer ({string.Join(", ", question.Answer)}), so agreed must be false.");
    }

    /// <summary>Every checks/&lt;id&gt;/ folder must belong to a question file.</summary>
    private void CheckForOrphanChecks()
    {
        var checks = Path.Combine(_root, "checks");
        if (!Directory.Exists(checks)) return;

        var questionIds = QuestionFiles().Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var directory in Directory.EnumerateDirectories(checks).Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(directory);
            if (!questionIds.Contains(id))
                Error($"checks/{id}", $"No question has id '{id}'.");
        }
    }

    private IEnumerable<string> QuestionFiles()
    {
        var directory = Path.Combine(_root, "questions");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories)
            : [];
    }

    private string Relative(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

    private void Error(string file, string message) => _errors.Add(new Diagnostic(file, null, null, message));

    private void Error<T>(Loaded<T> item, string pointer, string message)
    {
        var position = item.Document.PositionOf(pointer);
        _errors.Add(new Diagnostic(item.Path, position.Line, position.Column, message));
    }
}
