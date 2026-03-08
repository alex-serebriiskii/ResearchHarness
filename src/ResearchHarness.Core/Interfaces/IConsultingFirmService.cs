namespace ResearchHarness.Core.Interfaces;

/// <summary>
/// Manages the consulting agent swarm for domain expertise.
/// Phase 1: interface only — no implementation. Phase 2+ adds consulting support.
/// </summary>
public interface IConsultingFirmService
{
    /// <summary>
    /// Returns a domain briefing given a theme and the Lead's uncertainty context.
    /// </summary>
    Task<string> GetDomainBriefingAsync(
        string theme,
        string uncertaintyContext,
        CancellationToken ct = default);
}
