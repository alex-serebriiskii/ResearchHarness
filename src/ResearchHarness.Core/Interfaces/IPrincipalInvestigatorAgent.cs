using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Principal Investigator agent: breaks a topic into search tasks,
/// dispatches lab agents, and synthesizes a Paper.
/// </summary>
public interface IPrincipalInvestigatorAgent
{
    Task<Paper> ResearchTopicAsync(
        ResearchTopic topic,
        JobConfiguration config,
        CancellationToken ct = default);

    Task<Paper> ReviseTopicAsync(
        ResearchTopic topic,
        Paper currentPaper,
        List<ReviewResult> reviews,
        JobConfiguration config,
        CancellationToken ct = default);

}