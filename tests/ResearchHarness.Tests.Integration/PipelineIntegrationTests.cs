using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

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

    [Test]
    public async Task HealthEndpoint_WithApiKeyConfigured_DoesNotRequireAuth()
    {
        // Arrange: configure app with a non-empty API key
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ApiKey", "test-key-12345");
            });

        using var client = factory.CreateClient();

        // Act: request /health without X-Api-Key header
        var response = await client.GetAsync("/health");

        // Assert: health endpoint is exempt from API key middleware
        ((int)response.StatusCode).Should().Be(200);
    }

    [Test]
    public async Task OpenApiEndpoint_InProduction_Returns404()
    {
        // Arrange: boot in Production so OpenAPI is not registered
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");
                builder.UseSetting("ApiKey", ""); // disable auth guard
            });

        using var client = factory.CreateClient();

        // Act: request OpenAPI spec
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert: not exposed in production
        ((int)response.StatusCode).Should().Be(404);
    }

    [Test]
    public async Task ScalarEndpoint_InProduction_Returns404()
    {
        // Arrange: boot in Production so Scalar UI is not registered
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");
                builder.UseSetting("ApiKey", ""); // disable auth guard
            });

        using var client = factory.CreateClient();

        // Act: request Scalar UI
        var response = await client.GetAsync("/scalar/v1");

        // Assert: not exposed in production
        ((int)response.StatusCode).Should().Be(404);
    }
}