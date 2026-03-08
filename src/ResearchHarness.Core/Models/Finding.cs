namespace ResearchHarness.Core.Models;

/// <summary>
/// A structured finding extracted by a lab agent for a specific sub-topic.
/// SourceRefs contains SourceId values from the paper's Bibliography.
/// </summary>
public record Finding(
    string SubTopic,
    string Summary,
    List<string> KeyPoints,
    List<Guid> SourceRefs,
    double RelevanceScore
);
