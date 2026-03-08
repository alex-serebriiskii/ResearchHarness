using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResearchHarness.Tests.Integration;

/// <summary>
/// Integration tests using WebApplicationFactory with mocked external services.
/// Phase 2: validates API key guard middleware.
/// Full pipeline tests (with mock LLM/search) are in task 2F.1.
/// </summary>
public class PipelineIntegrationTests
{
    [Test]
    public void Placeholder_IntegrationTestSuite_IsScaffolded()
    {
        // Keeps the project compilable. Real pipeline tests in 2F.1.
        true.Should().BeTrue();
    }

    [Test]
    public async Task InternalRoute_WithoutApiKey_Returns401()
    {
        // Arrange: configure app with a non-empty API key
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", "test-api-key-12345");
            });

        using var client = factory.CreateClient();

        // Act: request /internal/jobs without auth header
        var response = await client.GetAsync("/internal/jobs");

        // Assert: middleware returns 401
        ((int)response.StatusCode).Should().Be(401);
    }

    [Test]
    public async Task InternalRoute_WithCorrectApiKey_DoesNotReturn401()
    {
        const string apiKey = "test-api-key-12345";
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", apiKey);
            });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        // Act: request /internal/jobs with correct key
        var response = await client.GetAsync("/internal/jobs");

        // Assert: not 401 (may be 404 if route doesn't exist, but not auth failure)
        ((int)response.StatusCode).Should().NotBe(401);
    }

    [Test]
    public async Task InternalRoute_WithNoApiKeyConfigured_AllowsAll()
    {
        // When ApiKey config is empty, middleware skips auth
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", ""); // empty = no auth required
            });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/jobs");

        // Not 401 — middleware skips when key is empty
        ((int)response.StatusCode).Should().NotBe(401);
    }
}
