namespace ResearchHarness.Core.Models;

/// <summary>
/// A web source referenced by one or more findings.
/// SourceId is the primary key — Finding.SourceRefs contains these Guids.
/// </summary>
public record Source(
    Guid SourceId,
    string Url,
    string Title,
    string? Author,
    DateTimeOffset? PublishedDate,
    SourceCredibility Credibility,
    string CredibilityRationale
);
