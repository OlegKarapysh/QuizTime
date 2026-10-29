using QuizBank.Yaml;

namespace QuizBank.Validation;

/// <summary>A bank file that passed its schema: its path relative to the repo root, its YAML, and the value read from it.</summary>
public sealed record BankFile<T>(string Path, YamlJsonDocument Document, T Value);
