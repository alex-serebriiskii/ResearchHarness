namespace ResearchHarness.Core.Models;

public record ResearchTopic(
    Guid TopicId,
    string Title,
    string Scope,
    List<string> SuggestedSearchAngles,
    List<string> ExpectedSourceTypes,
    TopicStatus Status,
    Paper? Paper
);
