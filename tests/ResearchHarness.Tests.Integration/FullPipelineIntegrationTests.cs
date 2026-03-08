using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ResearchHarness.Core.Interfaces;

namespace ResearchHarness.Tests.Integration;

public class FullPipelineIntegrationTests
{
    private static WebApplicationFactory<Program> BuildFactory(MockLlmClient mockLlm)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Empty ApiKey disables the X-Api-Key guard (see ApiKeyMiddleware).
                builder.UseSetting("ApiKey", "");
                builder.UseSetting("Llm:Provider", "OpenRouter");
                builder.UseSetting("OpenRouter:ApiKey", "test-key");

                builder.ConfigureServices(services =>
                {
                    // Remove all ILlmClient registrations (provider-conditional code may
                    // register exactly one, but guard against duplicates defensively).
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(ILlmClient))
                        .ToList();
                    toRemove.ForEach(d => services.Remove(d));

                    services.AddSingleton<ILlmClient>(mockLlm);
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

    // Produces JSON that deserializes into TopicDecompositionOutput via snake_case policy.
    private static string BuildTopicDecompositionJson(params string[] titles)
    {
        var topicsJson = string.Join(",",
            titles.Select(t => $"{{\"title\":\"{t}\",\"scope\":\"scope for {t}\"}}"));
        return $"{{\"topics\":[{topicsJson}]}}";
    }
}
