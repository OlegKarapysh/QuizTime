using QuizBank.Validation;

namespace QuizBank.Tests;

public sealed class BankValidatorTests : IDisposable
{
    private readonly TestRepo _repo = new();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void Real_repo_is_valid()
    {
        var report = BankValidator.Validate(RealRepo.Root);

        Assert.Empty(report.Errors);
        Assert.True(report.Questions > 0);
    }

    [Fact]
    public void Sample_repo_is_valid()
    {
        var report = _repo.Validate();

        Assert.Empty(report.Errors);
        Assert.Equal((1, 3, 1, 1), (report.Roadmaps, report.Nodes, report.Concepts, report.Questions));
    }

    // Questions: references

    [Fact]
    public void Unknown_node_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "node: csharp", "node: missing");

        AssertError(TestRepo.QuestionPath, "node: missing", "Node 'missing' is not in roadmaps/sample.yaml.");
    }

    [Fact]
    public void Unknown_roadmap_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "roadmap: sample", "roadmap: other");

        AssertError(TestRepo.QuestionPath, "roadmap: other", "Roadmap 'other' does not exist in roadmaps/.");
    }

    [Fact]
    public void Unknown_concept_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "concept: operator-binding-static", "concept: missing");

        AssertError(TestRepo.QuestionPath, "concept: missing", "Concept 'missing' is not in the concept registry.");
    }

    [Fact]
    public void Area_must_match_the_concept()
    {
        _repo.Replace(TestRepo.QuestionPath, "area: C# language features", "area: Security");

        AssertError(TestRepo.QuestionPath, "area: Security",
            "Area 'Security' differs from the area of concept 'operator-binding-static' ('C# language features').");
    }

    [Fact]
    public void Node_must_be_listed_on_the_concept()
    {
        _repo.Replace(TestRepo.QuestionPath, "id: csharp-001", "id: linq-001");
        _repo.Replace(TestRepo.QuestionPath, "node: csharp", "node: linq");
        _repo.Replace(TestRepo.QuestionPath, "check: checks/csharp-001/check.cs", "check: checks/linq-001/check.cs");
        _repo.Move(TestRepo.QuestionPath, "questions/sample/linq/linq-001.yaml");
        _repo.Move(TestRepo.CheckPath, "checks/linq-001/check.cs");

        AssertError("questions/sample/linq/linq-001.yaml", "node: linq",
            "Concept 'operator-binding-static' does not list node 'linq'; add it to the concept in concepts/sample.yaml.");
    }

    // Questions: ids and paths

    [Fact]
    public void File_path_must_match_roadmap_node_and_id()
    {
        _repo.Move(TestRepo.QuestionPath, "questions/sample/linq/csharp-001.yaml");

        AssertError("questions/sample/linq/csharp-001.yaml", "id: csharp-001",
            "File must be at questions/sample/csharp/csharp-001.yaml to match its roadmap, node and id.");
    }

    [Fact]
    public void Id_must_start_with_the_node()
    {
        _repo.Replace(TestRepo.QuestionPath, "id: csharp-001", "id: linq-001");
        _repo.Move(TestRepo.QuestionPath, "questions/sample/csharp/linq-001.yaml");

        AssertError("questions/sample/csharp/linq-001.yaml", "id: linq-001", "Id must be 'csharp-NNN' to match its node.");
    }

    [Fact]
    public void Id_needs_a_three_digit_sequence()
    {
        _repo.Replace(TestRepo.QuestionPath, "id: csharp-001", "id: csharp-1");

        AssertError(TestRepo.QuestionPath, "id: csharp-1", "/id: Value does not match the pattern");
    }

    [Fact]
    public void Duplicate_question_ids_are_reported()
    {
        _repo.Write("questions/sample/linq/csharp-001.yaml", File.ReadAllText(Path.Combine(_repo.Root, TestRepo.QuestionPath)));

        var report = _repo.Validate();

        Assert.Contains(report.Errors, e => e.Message == $"Duplicate question id 'csharp-001' (also in {TestRepo.QuestionPath}).");
    }

    [Fact]
    public void Yml_extension_is_rejected()
    {
        _repo.Move(TestRepo.QuestionPath, "questions/sample/csharp/csharp-001.yml");

        AssertError("questions/sample/csharp/csharp-001.yml", null, "Use the .yaml extension.");
    }

    // Questions: schema rules

    [Fact]
    public void Unquoted_version_is_reported_at_its_line()
    {
        _repo.Replace(TestRepo.QuestionPath, """dotnet: "10.0" """.TrimEnd(), "dotnet: 10.0");

        AssertError(TestRepo.QuestionPath, "dotnet: 10.0", "/targets/dotnet: Value is \"integer\" but should be \"string\"");
    }

    [Fact]
    public void Unknown_property_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "stem:", "hint: obvious\nstem:");

        AssertError(TestRepo.QuestionPath, "hint: obvious", "/hint: Property is not allowed here");
    }

    [Fact]
    public void Invalid_date_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "checked: 2026-10-29", "checked: 2026-13-01");

        AssertError(TestRepo.QuestionPath, "checked: 2026-13-01", "/sources/0/checked: Value does not match format \"date\"");
    }

    [Fact]
    public void A_proof_source_is_required()
    {
        _repo.Replace(TestRepo.QuestionPath, "kind: proof", "kind: further");

        AssertError(TestRepo.QuestionPath, "- kind: further", "/sources: Needs at least one source with kind: proof.");
    }

    [Fact]
    public void Single_choice_needs_four_options()
    {
        _repo.Replace(TestRepo.QuestionPath, """  - { id: d, text: "Throws at run" }""" + "\n", "");

        AssertError(TestRepo.QuestionPath, "{ id: a,", "/options: Value should have at least 4 items");
    }

    [Fact]
    public void Multi_choice_needs_two_or_three_answers()
    {
        _repo.Replace(TestRepo.QuestionPath, "type: single-choice", "type: multi-choice");
        _repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: [a]");

        AssertError(TestRepo.QuestionPath, "answer: [a]", "/answer: Value should have at least 2 items");
    }

    [Fact]
    public void Answer_must_be_an_option_id()
    {
        _repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: e");

        AssertError(TestRepo.QuestionPath, "answer: e", "Answer 'e' is not an option id.");
    }

    [Fact]
    public void Option_ids_must_be_unique()
    {
        _repo.Replace(TestRepo.QuestionPath, "{ id: d,", "{ id: c,");

        AssertError(TestRepo.QuestionPath, "Throws at run", "Duplicate option id 'c'.");
    }

    [Fact]
    public void Ordering_answer_must_list_every_option()
    {
        _repo.Replace(TestRepo.QuestionPath, "type: single-choice", "type: ordering");
        _repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: [a, b, c]");

        AssertError(TestRepo.QuestionPath, "answer: [a, b, c]", "An ordering answer must list every option id exactly once.");
    }

    [Fact]
    public void Quarantined_needs_a_reason()
    {
        _repo.Replace(TestRepo.QuestionPath, "status: draft", "status: quarantined");

        AssertError(TestRepo.QuestionPath, "id: csharp-001", "Required properties [\"quarantine\"] are not present");
    }

    // Questions: verification

    [Fact]
    public void Verified_needs_blind_solve_and_audit()
    {
        _repo.Replace(TestRepo.QuestionPath, "status: draft", "status: verified");

        AssertError(TestRepo.QuestionPath, "  method: run", "/verification: Required properties [\"blind_solve\",\"audit\"] are not present");
    }

    [Fact]
    public void Verified_needs_an_agreeing_blind_solve()
    {
        _repo.Replace(TestRepo.QuestionPath, "status: draft", "status: verified");
        _repo.Replace(TestRepo.QuestionPath, "  check: checks/csharp-001/check.cs", """
              check: checks/csharp-001/check.cs
              blind_solve: { model: other-model, picked: b, agreed: false, date: 2026-10-29 }
              audit: { verdict: pass, date: 2026-10-29 }
            """.TrimEnd());

        AssertError(TestRepo.QuestionPath, "blind_solve:", "/verification/blind_solve/agreed: Value must be \"true\"");
    }

    [Fact]
    public void Agreed_must_match_the_blind_solve_pick()
    {
        _repo.Replace(TestRepo.QuestionPath, "  check: checks/csharp-001/check.cs", """
              check: checks/csharp-001/check.cs
              blind_solve: { model: other-model, picked: b, agreed: true, date: 2026-10-29 }
            """.TrimEnd());

        AssertError(TestRepo.QuestionPath, "blind_solve:",
            "The blind-solve pick (b) differs from the answer (a), so agreed must be false.");
    }

    [Fact]
    public void Check_file_must_exist()
    {
        _repo.Delete(TestRepo.CheckPath);

        AssertError(TestRepo.QuestionPath, "check: checks/", "checks/csharp-001/check.cs does not exist.");
    }

    [Fact]
    public void Check_path_must_follow_the_id()
    {
        _repo.Replace(TestRepo.QuestionPath, "check: checks/csharp-001/check.cs", "check: checks/other/check.cs");

        AssertError(TestRepo.QuestionPath, "check: checks/", "The check for method 'run' must be at checks/csharp-001/check.cs.");
    }

    [Fact]
    public void Docs_method_has_no_check()
    {
        _repo.Replace(TestRepo.QuestionPath, "method: run", "method: docs");

        AssertError(TestRepo.QuestionPath, "check: checks/", "/verification/check: Property is not allowed here");
    }

    [Fact]
    public void Check_without_a_question_is_reported()
    {
        _repo.Write("checks/ghost-001/check.cs", "return 0;\n");

        AssertError("checks/ghost-001", null, "No question has id 'ghost-001'.");
    }

    // Questions: lint

    [Fact]
    public void Option_more_than_1_3_times_the_shortest_is_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "Fails to build", "Fails to build at all");

        AssertError(TestRepo.QuestionPath, "Fails to build at all",
            "Option 'c' has 21 characters, more than 1.3 times the 11 of option 'a'; even out the option lengths so length gives nothing away.");
    }

    [Fact]
    public void Options_exactly_1_3_times_the_shortest_are_allowed()
    {
        _repo.Replace(TestRepo.QuestionPath, "Prints True", "Prints Yes");
        _repo.Replace(TestRepo.QuestionPath, "Fails to build", "Doesn't build");

        Assert.Empty(_repo.Validate().Errors);
    }

    [Fact]
    public void Multi_choice_option_lengths_are_checked()
    {
        _repo.Replace(TestRepo.QuestionPath, "type: single-choice", "type: multi-choice");
        _repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: [a, b]");
        _repo.Replace(TestRepo.QuestionPath, "Fails to build", "Fails to build at all");

        AssertError(TestRepo.QuestionPath, "Fails to build at all", "Option 'c' has 21 characters");
    }

    [Fact]
    public void Ordering_option_lengths_are_not_checked()
    {
        _repo.Replace(TestRepo.QuestionPath, "type: single-choice", "type: ordering");
        _repo.Replace(TestRepo.QuestionPath, "answer: a", "answer: [a, b, c, d]");
        _repo.Replace(TestRepo.QuestionPath, "Fails to build", "Fails to build at all");

        Assert.Empty(_repo.Validate().Errors);
    }

    [Fact]
    public void Correct_option_longest_in_more_than_35_percent_is_reported()
    {
        // The sample's answer is the shortest option; option c is the longest.
        AddSampleCopies(19, answerLongestInFirst: 8);

        AssertError("questions", null,
            "The correct option is the longest in 8 of 20 single-choice questions (40%), more than the 35% allowed; " +
            "shorten the correct option or lengthen a wrong one in some of: csharp-002, csharp-003, csharp-004, " +
            "csharp-005, csharp-006, csharp-007, csharp-008, csharp-009.");
    }

    [Fact]
    public void Correct_option_longest_in_35_percent_is_allowed()
    {
        AddSampleCopies(19, answerLongestInFirst: 7);

        Assert.Empty(_repo.Validate().Errors);
    }

    [Fact]
    public void Longest_correct_share_needs_20_questions()
    {
        AddSampleCopies(18, answerLongestInFirst: 18);

        Assert.Empty(_repo.Validate().Errors);
    }

    [Fact]
    public void Retired_questions_do_not_count_toward_the_longest_correct_share()
    {
        // 8 of 21 is too many; without the retired csharp-002 it's 7 of 20.
        AddSampleCopies(20, answerLongestInFirst: 8);
        _repo.Replace("questions/sample/csharp/csharp-002.yaml", "status: draft", "status: retired");

        Assert.Empty(_repo.Validate().Errors);
    }

    [Fact]
    public void A_tie_for_longest_does_not_count_as_longest()
    {
        AddSampleCopies(19, answerLongestInFirst: 8);
        for (var i = 2; i <= 9; i++)
            _repo.Replace($"questions/sample/csharp/csharp-{i:000}.yaml", "Throws at run", "Throws at once");

        Assert.Empty(_repo.Validate().Errors);
    }

    [Theory]
    [InlineData("https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/",
        "Pin ASP.NET Core docs to version 10: add ?view=aspnetcore-10.0 to the URL.")]
    [InlineData("https://learn.microsoft.com/aspnet/core/fundamentals/middleware/?view=aspnetcore-9.0",
        "Pin ASP.NET Core docs to version 10: add ?view=aspnetcore-10.0 to the URL.")]
    [InlineData("https://learn.microsoft.com/en-us/dotnet/api/system.string.intern",
        "Pin API reference links to a .NET 10 view: add ?view= with one of net-10.0, net-10.0-pp, aspnetcore-10.0, efcore-10.0.")]
    [InlineData("https://learn.microsoft.com/dotnet/api/system.string.intern?view=net-9.0",
        "Pin API reference links to a .NET 10 view: add ?view= with one of net-10.0, net-10.0-pp, aspnetcore-10.0, efcore-10.0.")]
    [InlineData("https://www.postgresql.org/docs/current/functions-subquery.html",
        "Link the PostgreSQL 16 docs: use /docs/16/ instead of /docs/current/.")]
    public void Docs_links_must_pin_the_target_versions(string url, string message)
    {
        _repo.Replace(TestRepo.QuestionPath, "https://learn.microsoft.com/dotnet/api/system.string.intern?view=net-10.0", url);

        AssertError(TestRepo.QuestionPath, "url:", message);
    }

    [Theory]
    [InlineData("https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0#order")]
    [InlineData("https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbcontext.update?view=efcore-10.0")]
    [InlineData("https://learn.microsoft.com/en-us/ef/core/change-tracking/")]
    [InlineData("https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/equality-operators")]
    [InlineData("https://www.postgresql.org/docs/16/functions-subquery.html#FUNCTIONS-SUBQUERY-NOTIN")]
    public void Pinned_and_unversioned_docs_links_are_allowed(string url)
    {
        _repo.Replace(TestRepo.QuestionPath, "https://learn.microsoft.com/dotnet/api/system.string.intern?view=net-10.0", url);

        Assert.Empty(_repo.Validate().Errors);
    }

    // Denylist

    [Fact]
    public void Denylisted_name_is_reported_without_repeating_it()
    {
        _repo.Write("private/denylist.txt", "# Clients\nContoso\n");
        _repo.Replace(TestRepo.QuestionPath, "stem: What does this print?", "stem: What does the Contoso app print?");

        var report = _repo.Validate();

        var error = Assert.Single(report.Errors);
        Assert.Equal(
            $"{TestRepo.QuestionPath}:{_repo.LineOf(TestRepo.QuestionPath, "stem:")}:21: Matches the name on line 2 of the denylist; " +
            "use a neutral name such as Shop, Orders or Billing.",
            error.ToString());
        Assert.True(report.DenylistChecked);
    }

    [Theory]
    [InlineData("the contoso app", true)]
    [InlineData("CONTOSO's orders", true)]
    [InlineData("Contoso.Orders", true)]
    [InlineData("class ContosoShop", true)]
    [InlineData("var myContosoClient", true)]
    [InlineData("MyContoso", true)]
    [InlineData("Contosos", false)]
    [InlineData("contososhop", false)]
    [InlineData("NotContosoLike", true)]
    public void Denylist_matches_whole_words_and_camel_case_parts_in_any_case(string text, bool matches)
    {
        var denylist = Denylist.Parse(["Contoso"]);

        Assert.Equal(matches, denylist.Matches(text).Any());
    }

    [Fact]
    public void Denylist_covers_checks()
    {
        _repo.Write("private/denylist.txt", "Contoso\n");
        _repo.Write(TestRepo.CheckPath, "// Contoso\nreturn 0;\n");

        AssertError(TestRepo.CheckPath, "// Contoso", "Matches the name on line 1 of the denylist");
    }

    [Fact]
    public void Denylist_can_live_outside_the_root()
    {
        // The pre-commit hook validates a copy of the staged files, which has no private/ folder.
        using var other = new TestRepo();
        other.Write("private/denylist.txt", "print\n");

        var report = BankValidator.Validate(_repo.Root, Path.Combine(other.Root, "private", "denylist.txt"));

        Assert.Contains(report.Errors, e => e.File == TestRepo.QuestionPath && e.Message.StartsWith("Matches the name on line 1"));
    }

    [Fact]
    public void Missing_denylist_skips_the_name_check()
    {
        var report = _repo.Validate();

        Assert.False(report.DenylistChecked);
    }

    // Concepts

    [Fact]
    public void Concept_area_is_checked_through_the_shared_schema()
    {
        _repo.Replace(TestRepo.ConceptsPath, "area: C# language features", "area: Testing");

        AssertError(TestRepo.ConceptsPath, "area: Testing", "/0/area: Value \"Testing\" is not one of");
    }

    [Fact]
    public void Concept_nodes_must_exist()
    {
        _repo.Replace(TestRepo.ConceptsPath, "nodes: [csharp]", "nodes: [csharp, missing]");

        AssertError(TestRepo.ConceptsPath, "nodes:", "Node 'missing' is not in any roadmap.");
    }

    [Fact]
    public void Duplicate_concept_ids_are_reported()
    {
        _repo.Write("concepts/sample2.yaml", File.ReadAllText(Path.Combine(_repo.Root, TestRepo.ConceptsPath)));

        AssertError("concepts/sample2.yaml", "- id:", "Duplicate concept id 'operator-binding-static' (also in concepts/sample.yaml).");
    }

    // Roadmaps

    [Fact]
    public void Roadmap_id_must_match_the_file_name()
    {
        _repo.Replace(TestRepo.RoadmapPath, "id: sample", "id: other");

        AssertError(TestRepo.RoadmapPath, "id: other", "Roadmap id 'other' must match the file name 'sample'.");
    }

    [Fact]
    public void Duplicate_node_ids_are_reported()
    {
        _repo.Replace(TestRepo.RoadmapPath, "- id: linq", "- id: csharp");

        AssertError(TestRepo.RoadmapPath, "title: LINQ", "Duplicate node id 'csharp'.", lineOffset: -1);
    }

    [Fact]
    public void Parent_must_exist()
    {
        _repo.Replace(TestRepo.RoadmapPath, "title: LINQ\n    parent: language", "title: LINQ\n    parent: missing");

        AssertError(TestRepo.RoadmapPath, "parent: missing", "Parent 'missing' is not a node in this roadmap.");
    }

    [Fact]
    public void Parent_loops_are_reported()
    {
        _repo.Replace(TestRepo.RoadmapPath, "title: Language", "title: Language\n    parent: csharp");

        var report = _repo.Validate();

        Assert.Contains(report.Errors, e => e.Message == "The parent chain of 'language' loops back to 'language'.");
    }

    [Fact]
    public void Mirror_cannot_exceed_total_nodes()
    {
        _repo.Replace(TestRepo.RoadmapPath, "total_nodes: 5", "total_nodes: 2");

        AssertError(TestRepo.RoadmapPath, "source:", "The mirror holds 3 nodes, more than total_nodes (2).");
    }

    [Fact]
    public void Broken_roadmap_does_not_cascade_into_reference_errors()
    {
        _repo.Replace(TestRepo.RoadmapPath, "areas: [C# language features]", "areas: [C#, language features]");

        var report = _repo.Validate();

        Assert.NotEmpty(report.Errors);
        Assert.All(report.Errors, e => Assert.Equal(TestRepo.RoadmapPath, e.File));
    }

    // YAML

    [Fact]
    public void Yaml_syntax_errors_are_reported_with_a_position()
    {
        _repo.Replace(TestRepo.QuestionPath, "stem: What does this print?", "stem: [unclosed");

        var error = Assert.Single(_repo.Validate().Errors);
        Assert.Equal(TestRepo.QuestionPath, error.File);
        Assert.NotNull(error.Line);
        Assert.DoesNotContain("(Line:", error.Message);
    }

    [Fact]
    public void Duplicate_yaml_keys_are_reported()
    {
        _repo.Replace(TestRepo.QuestionPath, "level: intermediate", "level: intermediate\nlevel: advanced");

        AssertError(TestRepo.QuestionPath, "level: advanced", "Duplicate key level");
    }

    /// <summary>
    /// Adds copies of the sample question as csharp-002 onward, each with its check. The first
    /// <paramref name="answerLongestInFirst"/> copies mark the longest option (c) as the answer.
    /// </summary>
    private void AddSampleCopies(int count, int answerLongestInFirst)
    {
        var sample = File.ReadAllText(Path.Combine(_repo.Root, TestRepo.QuestionPath));
        for (var i = 0; i < count; i++)
        {
            var id = $"csharp-{i + 2:000}";
            var question = sample.Replace("csharp-001", id);
            if (i < answerLongestInFirst) question = question.Replace("answer: a", "answer: c");
            _repo.Write($"questions/sample/csharp/{id}.yaml", question);
            _repo.Write($"checks/{id}/check.cs", "return 0;\n");
        }
    }

    /// <summary>
    /// Asserts an error in <paramref name="file"/> that contains <paramref name="message"/>, located on the line holding
    /// <paramref name="lineText"/> (or with no line when <paramref name="lineText"/> is null).
    /// </summary>
    private void AssertError(string file, string? lineText, string message, int lineOffset = 0)
    {
        var report = _repo.Validate();
        int? line = lineText is null ? null : _repo.LineOf(file, lineText) + lineOffset;

        Assert.True(
            report.Errors.Any(e => e.File == file && e.Line == line && e.Message.Contains(message, StringComparison.Ordinal)),
            $"Expected {file}:{line} \"{message}\". Got:\n{string.Join("\n", report.Errors)}");
    }
}
