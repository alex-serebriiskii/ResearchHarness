using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Agents;

public class ConsultingFirmService : IConsultingFirmService
{
    private readonly ILlmClient _llm;
    private readonly JobConfiguration _config;
    private readonly ILogger<ConsultingFirmService> _logger;

    public ConsultingFirmService(
        ILlmClient llm,
        JobConfiguration config,
        ILogger<ConsultingFirmService> logger)
    {
        _llm = llm;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetDomainBriefingAsync(
        string theme,
        string uncertaintyContext,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Consulting firm generating domain briefing for theme: {Theme}", theme);

        var request = new LlmRequest(
            Model: _config.LeadModel,
            SystemPrompt: ConsultingBriefingPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(ConsultingBriefingPrompt.BuildUserMessage(theme, uncertaintyContext))]
        );

        var response = await _llm.CompleteAsync(request, ct);
        return response.Content;
    }
}
