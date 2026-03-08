namespace ResearchHarness.Core.Models;

public record Journal(
    string OverallSummary,
    string CrossTopicAnalysis,
    List<Paper> Papers,
    List<Source> MasterBibliography,
    DateTimeOffset AssembledAt
);
