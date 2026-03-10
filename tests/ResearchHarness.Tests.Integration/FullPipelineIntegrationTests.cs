using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;
namespace ResearchHarness.Tests.Integration;

public class FullPipelineIntegrationTests
{
    private static WebApplicationFactory<Program> BuildFactory(MockLlmClient mockLlm)
    {
        return BuildFactory(mockLlm, configureServices: null, configureSettings: null);
    }

    private static WebApplicationFactory<Program> BuildFactory(
        MockLlmClient mockLlm,
        Action<IServiceCollection>? configureServices,
        Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configureSettings)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Empty ApiKey disables the X-Api-Key guard (see ApiKeyMiddleware).
                builder.UseSetting("ApiKey", "");
                builder.UseSetting("Llm:Provider", "OpenRouter");
                builder.UseSetting("OpenRouter:ApiKey", "test-key");

                configureSettings?.Invoke(builder);

                builder.ConfigureServices(services =>
                {
                    // Remove all ILlmClient registrations (provider-conditional code may
                    // register exactly one, but guard against duplicates defensively).
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(ILlmClient))
                        .ToList();
                    toRemove.ForEach(d => services.Remove(d));

                    services.AddSingleton<ILlmClient>(mockLlm);

                    configureServices?.Invoke(services);
                });
            });
    }

    [Test]
    public async Task PostJob_WithMockLlm_ReturnsJobId()
    {
        // Arrange — default response is "{}" which agents tolerate as empty output.
        var mockLlm = new MockLlmClient
        {
            DefaultResponse = BuildTopicDecompositionJson("quantum error correction"),
        };
        using var factory = BuildFactory(mockLlm);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/internal/research/start",
            new { theme = "quantum computing" });

        // Assert: job is accepted and a GUID is returned.
        ((int)response.StatusCode).Should().BeInRange(200, 299);

        var body = await response.Content.ReadAsStringAsync();
        Guid.TryParse(body.Trim('"'), out var jobId).Should().BeTrue(
            $"Expected a GUID but got: {body}");
        jobId.Should().NotBe(Guid.Empty);
    }

    [Test]
    public async Task GetStatus_WithUnknownJobId_Returns404()
    {
        var mockLlm = new MockLlmClient();
        using var factory = BuildFactory(mockLlm);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/internal/research/{Guid.NewGuid()}/status");

        // Assert
        ((int)response.StatusCode).Should().Be(404);
    }

    [Test]
    public async Task PostJob_WithEmptyTheme_Returns400()
    {
        var mockLlm = new MockLlmClient();
        using var factory = BuildFactory(mockLlm);
        using var client = factory.CreateClient();

        // Act — empty theme should fail validation in the controller.
        var response = await client.PostAsJsonAsync(
            "/internal/research/start",
            new { theme = "" });

        // Assert
        ((int)response.StatusCode).Should().Be(400);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Produces JSON that deserializes into TopicDecompositionOutput via snake_case policy.
    private static string BuildTopicDecompositionJson(params string[] titles)
    {
        var topicsJson = string.Join(",",
            titles.Select(t => $"{{\"title\":\"{t}\",\"scope\":\"scope for {t}\"}}"));
        return $"{{\"topics\":[{topicsJson}]}}";
    }

    private static void ReplaceService<T>(IServiceCollection services, T instance) where T : class
    {
        var toRemove = services.Where(d => d.ServiceType == typeof(T)).ToList();
        toRemove.ForEach(d => services.Remove(d));
        services.AddSingleton(instance);
    }

    private static readonly JsonSerializerOptions StatusJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Polls the status endpoint until the job reaches a terminal state or the timeout expires.
    /// </summary>
    private static async Task<JobStatus> PollUntilTerminalAsync(
        HttpClient client, Guid jobId, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var resp = await client.GetAsync($"/internal/research/{jobId}/status");
            ((int)resp.StatusCode).Should().Be(200, $"status poll for {jobId}");

            var body = await resp.Content.ReadAsStringAsync();
            var status = JsonSerializer.Deserialize<JobStatus>(body, StatusJsonOptions);

            if (status is JobStatus.Completed or JobStatus.Failed)
                return status;

            await Task.Delay(100);
        }

        throw new TimeoutException($"Job {jobId} did not reach terminal state within {timeout}");
    }

    // ── Full Pipeline Tests ─────────────────────────────────────────────────

    [Test]
    public async Task FullPipeline_SubmitAndComplete_ReturnsValidJournal()
    {
        // Arrange: queue deterministic LLM responses in exact pipeline call order.
        // Config: MaxTopics=1, PeerReviewerCount=0, EnableConsultingFirm=false
        // Pipeline stages: Decomposition → TaskBreakdown → LabExtraction → Synthesis → Assembly
        var mockLlm = new MockLlmClient();

        // 1. Theme decomposition → 1 topic
        mockLlm.Enqueue("{\"topics\":[{\"title\":\"Test Topic\",\"scope\":\"Test scope\"}]}");

        // 2. Task breakdown → 1 search task (FetchPageContent defaults to false)
        mockLlm.Enqueue("{\"tasks\":[{\"query\":\"test query\",\"extraction_instructions\":\"extract findings\",\"relevance_criteria\":\"must be relevant\"}]}");

        // 3. Lab extraction → 1 finding + 1 source
        mockLlm.Enqueue("{\"findings\":[{\"sub_topic\":\"AI\",\"summary\":\"Test finding summary\",\"key_points\":[\"key point 1\"],\"source_url\":\"https://example.com/article\",\"relevance_score\":0.85}],\"sources\":[{\"url\":\"https://example.com/article\",\"title\":\"Example Source\",\"credibility\":\"High\",\"credibility_rationale\":\"Established institution\"}]}");

        // 4. Paper synthesis → executive summary + confidence
        mockLlm.Enqueue("{\"executive_summary\":\"This is the executive summary.\",\"confidence_score\":0.85}");

        // 5. Journal assembly → overall summary + cross-topic analysis
        mockLlm.Enqueue("{\"overall_summary\":\"Overall summary of research.\",\"cross_topic_analysis\":\"Cross-topic analysis.\"}");

        // Stub search provider: returns 1 hit so the lab agent has data to extract from.
        var stubSearch = new StubSearchProvider(
            new SearchResults([new SearchHit(
                "https://example.com/article",
                "Example Source",
                "A snippet about the topic.",
                DateTimeOffset.UtcNow)], null));

        // Stub page fetcher: returns empty content (FetchPageContent=false means it won't be called,
        // but register it to prevent real HTTP calls if the flag were ever true).
        var stubFetcher = new StubPageFetcher();

        using var factory = BuildFactory(mockLlm,
            configureServices: services =>
            {
                ReplaceService<ISearchProvider>(services, stubSearch);
                ReplaceService<IPageFetcher>(services, stubFetcher);
            },
            configureSettings: builder =>
            {
                builder.UseSetting("Research:MaxTopics", "1");
                builder.UseSetting("Research:PeerReviewerCount", "0");
                builder.UseSetting("Research:EnableConsultingFirm", "false");
                builder.UseSetting("Research:MaxLabAgentsPerPI", "1");
            });

        using var client = factory.CreateClient();

        // Act: submit the job
        var startResp = await client.PostAsJsonAsync(
            "/internal/research/start",
            new { theme = "integration test theme" });

        ((int)startResp.StatusCode).Should().BeInRange(200, 299);
        var jobIdStr = await startResp.Content.ReadAsStringAsync();
        Guid.TryParse(jobIdStr.Trim('"'), out var jobId).Should().BeTrue(
            $"Expected a GUID but got: {jobIdStr}");

        // Poll until complete (background processor runs asynchronously)
        var finalStatus = await PollUntilTerminalAsync(client, jobId, TimeSpan.FromSeconds(15));
        finalStatus.Should().Be(JobStatus.Completed);

        // Retrieve the journal
        var journalResp = await client.GetAsync($"/internal/research/{jobId}/journal");
        ((int)journalResp.StatusCode).Should().Be(200);

        var journalJson = await journalResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(journalJson);
        var root = doc.RootElement;

        // Assert journal structure
        root.GetProperty("overallSummary").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("crossTopicAnalysis").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("papers").GetArrayLength().Should().Be(1);

        var paper = root.GetProperty("papers")[0];
        paper.GetProperty("executiveSummary").GetString().Should().NotBeNullOrEmpty();
        paper.GetProperty("findings").GetArrayLength().Should().BeGreaterThan(0);
        paper.GetProperty("bibliography").GetArrayLength().Should().BeGreaterThan(0);

        root.GetProperty("masterBibliography").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task FullPipeline_WithPeerReview_RevisesAndCompletes()
    {
        // Same as above but with PeerReviewerCount=1, adding a review response.
        // Pipeline: Decomposition → TaskBreakdown → LabExtraction → Synthesis → PeerReview → Assembly
        var mockLlm = new MockLlmClient();

        // 1. Theme decomposition
        mockLlm.Enqueue("{\"topics\":[{\"title\":\"Review Topic\",\"scope\":\"Review scope\"}]}");

        // 2. Task breakdown
        mockLlm.Enqueue("{\"tasks\":[{\"query\":\"review query\",\"extraction_instructions\":\"extract\",\"relevance_criteria\":\"relevant\"}]}");

        // 3. Lab extraction
        mockLlm.Enqueue("{\"findings\":[{\"sub_topic\":\"ML\",\"summary\":\"ML finding\",\"key_points\":[\"point\"],\"source_url\":\"https://example.com/ml\",\"relevance_score\":0.9}],\"sources\":[{\"url\":\"https://example.com/ml\",\"title\":\"ML Source\",\"credibility\":\"High\",\"credibility_rationale\":\"Top journal\"}]}");

        // 4. Paper synthesis
        mockLlm.Enqueue("{\"executive_summary\":\"Initial synthesis.\",\"confidence_score\":0.8}");

        // 5. Peer review → Accept verdict (1 reviewer)
        mockLlm.Enqueue("{\"verdict\":\"Accept\",\"feedback\":\"Well done.\",\"issues\":[]}");

        // 6. Journal assembly
        mockLlm.Enqueue("{\"overall_summary\":\"Reviewed summary.\",\"cross_topic_analysis\":\"Reviewed analysis.\"}");

        var stubSearch = new StubSearchProvider(
            new SearchResults([new SearchHit(
                "https://example.com/ml",
                "ML Source",
                "ML snippet.",
                DateTimeOffset.UtcNow)], null));

        using var factory = BuildFactory(mockLlm,
            configureServices: services =>
            {
                ReplaceService<ISearchProvider>(services, stubSearch);
                ReplaceService<IPageFetcher>(services, new StubPageFetcher());
            },
            configureSettings: builder =>
            {
                builder.UseSetting("Research:MaxTopics", "1");
                builder.UseSetting("Research:PeerReviewerCount", "1");
                builder.UseSetting("Research:EnableConsultingFirm", "false");
                builder.UseSetting("Research:MaxLabAgentsPerPI", "1");
            });

        using var client = factory.CreateClient();

        var startResp = await client.PostAsJsonAsync(
            "/internal/research/start",
            new { theme = "peer review integration test" });

        ((int)startResp.StatusCode).Should().BeInRange(200, 299);
        var jobIdStr = await startResp.Content.ReadAsStringAsync();
        Guid.TryParse(jobIdStr.Trim('"'), out var jobId).Should().BeTrue();

        var finalStatus = await PollUntilTerminalAsync(client, jobId, TimeSpan.FromSeconds(15));
        finalStatus.Should().Be(JobStatus.Completed);

        // Verify journal exists and has expected structure
        var journalResp = await client.GetAsync($"/internal/research/{jobId}/journal");
        ((int)journalResp.StatusCode).Should().Be(200);

        var journalJson = await journalResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(journalJson);
        doc.RootElement.GetProperty("overallSummary").GetString().Should().Be("Reviewed summary.");
        doc.RootElement.GetProperty("papers").GetArrayLength().Should().Be(1);

        // The paper should have reviews since PeerReviewerCount=1
        var paper = doc.RootElement.GetProperty("papers")[0];
        paper.GetProperty("reviews").GetArrayLength().Should().Be(1);
    }
}

// ── Test stubs for ISearchProvider / IPageFetcher ────────────────────────────

/// <summary>
/// Returns a fixed set of search results for any query.
/// </summary>
internal sealed class StubSearchProvider : ISearchProvider
{
    private readonly SearchResults _results;

    public StubSearchProvider(SearchResults results) => _results = results;

    public Task<SearchResults> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(_results);
}

/// <summary>
/// Returns empty page content for any URL. Prevents real HTTP calls.
/// </summary>
internal sealed class StubPageFetcher : IPageFetcher
{
    public Task<PageContent> FetchAsync(string url, CancellationToken ct = default)
        => Task.FromResult(new PageContent(url, string.Empty, null, null));
}