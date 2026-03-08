using System.Text.Json.Serialization;

namespace ResearchHarness.Infrastructure.Search.Dto;

public record BraveSearchResponse(
    [property: JsonPropertyName("web")] BraveWebResults? Web
);

public record BraveWebResults(
    [property: JsonPropertyName("results")] List<BraveWebResult> Results
);

public record BraveWebResult(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("age")] string? Age
);
