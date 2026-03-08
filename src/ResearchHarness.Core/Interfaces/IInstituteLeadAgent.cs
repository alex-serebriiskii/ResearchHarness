using ResearchHarness.Core.Models;

namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// The Institute Lead agent: decomposes a research theme into topics
/// and assembles the final journal once all papers are ready.
/// </summary>
public interface IInstituteLeadAgent
{
    /// <summary>
    /// Decomposes a research theme into a list of ResearchTopics.
    /// </summary>
    Task<List<ResearchTopic>> DecomposeThemeAsync(
        string theme,
        JobConfiguration config,
        string? domainContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// Assembles accepted papers into a final Journal.
    /// </summary>
    Task<Journal> AssembleJournalAsync(
        string theme,
        List<Paper> papers,
        CancellationToken ct = default);
}
