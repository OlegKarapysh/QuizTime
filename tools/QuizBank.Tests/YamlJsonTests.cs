using System.Text.Json;
using QuizBank.Yaml;

namespace QuizBank.Tests;

public sealed class YamlJsonTests
{
    [Theory]
    [InlineData("v: true", "true")]
    [InlineData("v: 10", "10")]
    [InlineData("v: 10.5", "10.5")]
    [InlineData("v: \"10.0\"", "\"10.0\"")]
    [InlineData("v: '7'", "\"7\"")]
    [InlineData("v: 2026-11-02", "\"2026-11-02\"")]
    [InlineData("v: yes", "\"yes\"")]
    [InlineData("v: ~", "null")]
    [InlineData("v:", "null")]
    [InlineData("v: !!str 10", "\"10\"")]
    [InlineData("v: |\n  line\n", "\"line\\n\"")]
    public void Scalars_resolve_like_the_yaml_core_schema(string yaml, string json)
    {
        var document = YamlJson.Parse(yaml);

        Assert.Equal(json, document.Root["v"]?.ToJsonString() ?? "null");
    }

    [Fact]
    public void Positions_are_recorded_per_json_pointer()
    {
        var document = YamlJson.Parse("""
            id: x
            options:
              - { id: a, text: "one" }
              - id: b
                text: two
            """);

        Assert.Equal(new SourcePosition(1, 5), document.PositionOf("/id"));
        Assert.Equal(new SourcePosition(3, 20), document.PositionOf("/options/0/text"));
        Assert.Equal(new SourcePosition(5, 11), document.PositionOf("/options/1/text"));
    }

    [Fact]
    public void Missing_pointers_fall_back_to_the_nearest_ancestor()
    {
        var document = YamlJson.Parse("a:\n  b: 1\n");

        Assert.Equal(document.PositionOf("/a/b"), document.PositionOf("/a/b/c/d"));
        Assert.Equal(new SourcePosition(1, 1), document.PositionOf("/missing"));
    }

    [Fact]
    public void Keys_with_slashes_are_escaped_in_pointers()
    {
        var document = YamlJson.Parse("\"a/b\": 1\n");

        Assert.Equal(new SourcePosition(1, 8), document.PositionOf("/a~1b"));
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        var error = Assert.Throws<YamlLoadException>(() => YamlJson.Parse("a: 1\nb: 2\na: 3\n"));

        Assert.Equal(3, error.Position.Line);
        Assert.Equal("Duplicate key a", error.Message);
    }

    [Fact]
    public void Syntax_errors_carry_a_position_and_a_clean_message()
    {
        var error = Assert.Throws<YamlLoadException>(() => YamlJson.Parse("a: \"unclosed\n"));

        Assert.Equal(new SourcePosition(1, 4), error.Position);
        Assert.Equal("While scanning a quoted scalar, found unexpected end of stream.", error.Message);
    }

    [Fact]
    public void Unclosed_flow_sequences_are_reported_instead_of_crashing()
    {
        var error = Assert.Throws<YamlLoadException>(() => YamlJson.Parse("a: [1, 2\nb: 3\n"));

        Assert.Equal(new SourcePosition(1, 5), error.Position);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    [InlineData("~\n")]
    public void Empty_files_are_rejected(string yaml)
    {
        var error = Assert.Throws<YamlLoadException>(() => YamlJson.Parse(yaml));

        Assert.Equal("File is empty.", error.Message);
    }

    [Fact]
    public void Multiple_documents_are_rejected()
    {
        var error = Assert.Throws<YamlLoadException>(() => YamlJson.Parse("a: 1\n---\nb: 2\n"));

        Assert.Equal(3, error.Position.Line);
    }

    [Fact]
    public void Converted_documents_serialize_to_plain_json()
    {
        var document = YamlJson.Parse("targets: { dotnet: \"10.0\" }\nanswer: [a, b]\n");

        Assert.Equal("""{"targets":{"dotnet":"10.0"},"answer":["a","b"]}""",
            JsonSerializer.Serialize(document.Root));
    }
}
