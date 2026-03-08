namespace ResearchHarness.Core.Models;

/// <summary>
/// A discrete search task produced by the PI's task breakdown.
/// The PI instructs each lab agent via this record.
/// </summary>
public record SearchTask(
    string Query,
    List<string> TargetSourceTypes,
    string ExtractionInstructions,
    string RelevanceCriteria,
    bool FetchPageContent = false
);
