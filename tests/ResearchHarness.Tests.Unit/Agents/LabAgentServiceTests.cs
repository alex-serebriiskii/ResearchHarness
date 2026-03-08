using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ResearchHarness.Agents;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Tests.Unit.Agents;

public class LabAgentServiceTests
{
    private ILlmClient _llm = null!;
    private ISearchProvider _search = null!;
    private IPageFetcher _pageFetcher = null!;
    private LabAgentService _lab = null!;
    private JobConfiguration _config = null!;

    private static readonly SearchTask BasicTask = new(
        Query: "AI drug discovery 2025",
        TargetSourceTypes: ["academic", "news"],
        ExtractionInstructions: "Extract key findings about AI in drug discovery",
        RelevanceCriteria: "Must discuss ML or AI applied to drug development",
        FetchPageContent: false
    );

    [Before(Test)]
    public void Setup()
    {
        _llm = Substitute.For<ILlmClient>();
        _search = Substitute.For<ISearchProvider>();
        _pageFetcher = Substitute.For<IPageFetcher>();
        _config = new JobConfiguration(SearchResultsPerQuery: 5, LabModel: "claude-haiku-test");

        _lab = new LabAgentService(
            _llm,
            _search,
            _pageFetcher,
            Substitute.For<ILogger<LabAgentService>>()
        );
    }

    private SearchResults BuildSearchResults(int hitCount = 3) =>
        new(Enumerable.Range(1, hitCount).Select(i =>
            new SearchHit(
                $"https://source{i}.com/article",
                $"Source {i} Title",
                $"Snippet about AI drug discovery finding {i}",
                DateTimeOffset.UtcNow.AddDays(-i)
            )).ToList(), null);

    private void SetupLlmExtraction(int findingCount = 2, int sourceCount = 2)
    {
        var findings = Enumerable.Range(1, findingCount).Select(i =>
            new ExtractedFindingDto(
                $"SubTopic {i}",
                $"Summary of finding {i}",
                [$"Key point {i}a", $"Key point {i}b"],
                $"https://source{i}.com/article",
                0.7 + i * 0.05
            )).ToList();

        var sources = Enumerable.Range(1, sourceCount).Select(i =>
            new ExtractedSourceDto(
                $"https://source{i}.com/article",
                $"Source {i} Title",
                null,
                "High",
                "Established research institution"
            )).ToList();

        var output = new LabExtractionOutput(findings, sources);
        _llm.CompleteAsync<LabExtractionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<LabExtractionOutput>(output, new TokenUsage(200, 150), "tool_use"));
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_CallsSearchProvider()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction();

        await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        await _search.Received(1).SearchAsync(
            BasicTask.Query,
            Arg.Is<SearchOptions?>(o => o != null && o.Count == _config.SearchResultsPerQuery),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_ReturnsFindings()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction(findingCount: 2, sourceCount: 2);

        var result = await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        result.Findings.Should().HaveCount(2);
        result.Sources.Should().HaveCount(2);
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_FindingsHaveSourceRefs()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction(findingCount: 1, sourceCount: 1);

        var result = await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        result.Findings.Should().HaveCount(1);
        result.Findings[0].SourceRefs.Should().HaveCount(1);
        result.Sources.Should().HaveCount(1);

        // The source ref must match the source id
        result.Findings[0].SourceRefs[0].Should().Be(result.Sources[0].SourceId);
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_ParsesCredibility()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults(1));

        var output = new LabExtractionOutput(
        [
            new ExtractedFindingDto("sub", "summary", [], "https://s.com", 0.9)
        ],
        [
            new ExtractedSourceDto("https://s.com", "Title", null, "Medium", "Regional outlet")
        ]);

        _llm.CompleteAsync<LabExtractionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<LabExtractionOutput>(output, new TokenUsage(100, 100), "tool_use"));

        var result = await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        result.Sources[0].Credibility.Should().Be(SourceCredibility.Medium);
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_UnknownCredibility_FallsBackToUnknown()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults(1));

        var output = new LabExtractionOutput(
        [
            new ExtractedFindingDto("sub", "summary", [], "https://s.com", 0.5)
        ],
        [
            new ExtractedSourceDto("https://s.com", "Title", null, "VeryReputable", "rationale")
        ]);

        _llm.CompleteAsync<LabExtractionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<LabExtractionOutput>(output, new TokenUsage(100, 100), "tool_use"));

        var result = await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        result.Sources[0].Credibility.Should().Be(SourceCredibility.Unknown);
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_FetchPageContent_CallsPageFetcher()
    {
        var fetchTask = BasicTask with { FetchPageContent = true };
        var hits = BuildSearchResults(hitCount: 2);

        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(hits);
        _pageFetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PageContent("https://s.com", "Full page text", "Title", null));
        SetupLlmExtraction(1, 1);

        await _lab.ExecuteSearchTaskFullAsync(fetchTask, _config);

        await _pageFetcher.Received(2).FetchAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_NoFetchPageContent_NeverCallsPageFetcher()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction();

        await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        await _pageFetcher.DidNotReceive().FetchAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteSearchTaskAsync_DelegatesToFullAsync()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction(findingCount: 3, sourceCount: 3);

        var findings = await _lab.ExecuteSearchTaskAsync(BasicTask, _config);

        findings.Should().HaveCount(3);
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_UsesLabModel()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults());
        SetupLlmExtraction();

        await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        await _llm.Received(1).CompleteAsync<LabExtractionOutput>(
            Arg.Is<LlmRequest>(r => r.Model == _config.LabModel),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteSearchTaskFullAsync_FindingWithUnknownSourceUrl_CreatesFallbackSource()
    {
        _search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchOptions?>(), Arg.Any<CancellationToken>())
            .Returns(BuildSearchResults(1));

        // Finding references a URL not in the sources list
        var output = new LabExtractionOutput(
        [
            new ExtractedFindingDto("sub", "summary", [], "https://orphan.com", 0.8)
        ],
        [] // no sources in LLM output
        );

        _llm.CompleteAsync<LabExtractionOutput>(
                Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<LabExtractionOutput>(output, new TokenUsage(100, 100), "tool_use"));

        var result = await _lab.ExecuteSearchTaskFullAsync(BasicTask, _config);

        result.Findings.Should().HaveCount(1);
        result.Sources.Should().HaveCount(1);
        result.Sources[0].Url.Should().Be("https://orphan.com");
        result.Sources[0].Credibility.Should().Be(SourceCredibility.Unknown);
        result.Findings[0].SourceRefs[0].Should().Be(result.Sources[0].SourceId);
    }
}
