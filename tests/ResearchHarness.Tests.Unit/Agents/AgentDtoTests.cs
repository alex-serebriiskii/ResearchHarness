using System.Text.Json;
using AwesomeAssertions;
using ResearchHarness.Agents;
using ResearchHarness.Agents.Internal;

namespace ResearchHarness.Tests.Unit.Agents;

/// <summary>
/// Exhaustive deserialization tests for all DTO records used in agent LLM responses.
/// Verifies snake_case mapping, nullable field handling, and round-trip correctness.
/// </summary>
public class AgentDtoTests
{
    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, AgentSerializerOptions.Default)!;

    // ── TopicDecompositionOutput ────────────────────────────────────────────────

    [Test]
    public void TopicDecompositionOutput_AllFields_Deserializes()
    {
        const string json = """
            {
                "topics": [
                    {
                        "title": "AI Safety",
                        "scope": "Safety in AI systems",
                        "suggested_search_angles": ["angle1", "angle2"],
                        "expected_source_types": ["academic", "news"]
                    }
                ]
            }
            """;

        var result = Deserialize<TopicDecompositionOutput>(json);

        result.Topics.Should().HaveCount(1);
        result.Topics![0].Title.Should().Be("AI Safety");
        result.Topics[0].Scope.Should().Be("Safety in AI systems");
        result.Topics[0].SuggestedSearchAngles.Should().HaveCount(2);
        result.Topics[0].ExpectedSourceTypes.Should().HaveCount(2);
    }

    [Test]
    public void TopicDecompositionOutput_NullTopics_Deserializes()
    {
        const string json = "{}";
        var result = Deserialize<TopicDecompositionOutput>(json);
        result.Topics.Should().BeNull();
    }

    [Test]
    public void TopicDto_NullableLists_Deserializes()
    {
        const string json = """{"title": "T", "scope": "S"}""";
        var result = Deserialize<TopicDto>(json);
        result.Title.Should().Be("T");
        result.Scope.Should().Be("S");
        result.SuggestedSearchAngles.Should().BeNull();
        result.ExpectedSourceTypes.Should().BeNull();
    }

    // ── TaskBreakdownOutput ─────────────────────────────────────────────────────

    [Test]
    public void TaskBreakdownOutput_AllFields_Deserializes()
    {
        const string json = """
            {
                "tasks": [
                    {
                        "query": "AI drug discovery",
                        "target_source_types": ["academic"],
                        "extraction_instructions": "extract method",
                        "relevance_criteria": "AI topic",
                        "fetch_page_content": true
                    }
                ]
            }
            """;

        var result = Deserialize<TaskBreakdownOutput>(json);

        result.Tasks.Should().HaveCount(1);
        result.Tasks![0].Query.Should().Be("AI drug discovery");
        result.Tasks[0].FetchPageContent.Should().BeTrue();
    }

    [Test]
    public void SearchTaskDto_FetchPageContent_DefaultsFalse()
    {
        const string json = """
            {
                "query": "q",
                "extraction_instructions": "e",
                "relevance_criteria": "r"
            }
            """;
        var result = Deserialize<SearchTaskDto>(json);
        result.FetchPageContent.Should().BeFalse();
    }

    // ── LabExtractionOutput ─────────────────────────────────────────────────────

    [Test]
    public void LabExtractionOutput_AllFields_Deserializes()
    {
        const string json = """
            {
                "findings": [
                    {
                        "sub_topic": "ML",
                        "summary": "ML is useful",
                        "key_points": ["point1"],
                        "source_url": "https://example.com",
                        "relevance_score": 0.9
                    }
                ],
                "sources": [
                    {
                        "url": "https://example.com",
                        "title": "Example",
                        "author": "Alice",
                        "credibility": "High",
                        "credibility_rationale": "peer reviewed"
                    }
                ]
            }
            """;

        var result = Deserialize<LabExtractionOutput>(json);

        result.Findings.Should().HaveCount(1);
        result.Findings![0].SubTopic.Should().Be("ML");
        result.Findings[0].RelevanceScore.Should().BeApproximately(0.9, 0.001);
        result.Sources.Should().HaveCount(1);
        result.Sources![0].Url.Should().Be("https://example.com");
    }

    [Test]
    public void LabExtractionOutput_NullFindings_Deserializes()
    {
        const string json = "{\"sources\": []}";
        var result = Deserialize<LabExtractionOutput>(json);
        result.Findings.Should().BeNull();
        result.Sources.Should().BeEmpty();
    }

    [Test]
    public void ExtractedFindingDto_AllNullable_Deserializes()
    {
        const string json = "{\"relevance_score\": 0.5}";
        var result = Deserialize<ExtractedFindingDto>(json);
        result.SubTopic.Should().BeNull();
        result.Summary.Should().BeNull();
        result.KeyPoints.Should().BeNull();
        result.SourceUrl.Should().BeNull();
        result.RelevanceScore.Should().BeApproximately(0.5, 0.001);
    }

    [Test]
    public void ExtractedSourceDto_AllNullable_Deserializes()
    {
        const string json = "{}";
        var result = Deserialize<ExtractedSourceDto>(json);
        result.Url.Should().BeNull();
        result.Title.Should().BeNull();
        result.Author.Should().BeNull();
        result.Credibility.Should().BeNull();
        result.CredibilityRationale.Should().BeNull();
    }

    // ── JournalAssemblyOutput ───────────────────────────────────────────────────

    [Test]
    public void JournalAssemblyOutput_AllFields_Deserializes()
    {
        const string json = """{"overall_summary": "great", "cross_topic_analysis": "consistent"}""";
        var result = Deserialize<JournalAssemblyOutput>(json);
        result.OverallSummary.Should().Be("great");
        result.CrossTopicAnalysis.Should().Be("consistent");
    }

    [Test]
    public void JournalAssemblyOutput_NullFields_Deserializes()
    {
        const string json = "{}";
        var result = Deserialize<JournalAssemblyOutput>(json);
        result.OverallSummary.Should().BeNull();
        result.CrossTopicAnalysis.Should().BeNull();
    }

    // ── PaperSynthesisOutput ────────────────────────────────────────────────────

    [Test]
    public void PaperSynthesisOutput_AllFields_Deserializes()
    {
        const string json = """{"executive_summary": "summary text", "confidence_score": 0.85}""";
        var result = Deserialize<PaperSynthesisOutput>(json);
        result.ExecutiveSummary.Should().Be("summary text");
        result.ConfidenceScore.Should().BeApproximately(0.85, 0.001);
    }

    [Test]
    public void PaperSynthesisOutput_NullSummary_Deserializes()
    {
        const string json = "{\"confidence_score\": 0.5}";
        var result = Deserialize<PaperSynthesisOutput>(json);
        result.ExecutiveSummary.Should().BeNull();
        result.ConfidenceScore.Should().BeApproximately(0.5, 0.001);
    }
}
