namespace ResearchHarness.Agents.Internal;

internal record TopicDecompositionOutput(List<TopicDto> Topics);

internal record TopicDto(
    string Title,
    string Scope,
    List<string> SuggestedSearchAngles,
    List<string> ExpectedSourceTypes
);

internal record TaskBreakdownOutput(List<SearchTaskDto> Tasks);

internal record SearchTaskDto(
    string Query,
    List<string> TargetSourceTypes,
    string ExtractionInstructions,
    string RelevanceCriteria,
    bool FetchPageContent = false
);

internal record LabExtractionOutput(List<ExtractedFindingDto> Findings, List<ExtractedSourceDto> Sources);

internal record ExtractedFindingDto(
    string SubTopic,
    string Summary,
    List<string> KeyPoints,
    string SourceUrl,
    double RelevanceScore
);

internal record ExtractedSourceDto(
    string Url,
    string Title,
    string? Author,
    string Credibility,
    string CredibilityRationale
);

internal record JournalAssemblyOutput(string OverallSummary, string CrossTopicAnalysis);

internal record PaperSynthesisOutput(string ExecutiveSummary, double ConfidenceScore);
