using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Manages the peer review board.
/// Phase 1: interface only — no implementation. Phase 2+ adds reviewers.
/// </summary>
public interface IPeerReviewService
{
    Task<List<ReviewResult>> ReviewPaperAsync(
        Paper paper,
        ResearchTopic topic,
        JobConfiguration config,
        CancellationToken ct = default);
}
