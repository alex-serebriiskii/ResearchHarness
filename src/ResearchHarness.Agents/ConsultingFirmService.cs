using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Agents;

public partial class ConsultingFirmService : IConsultingFirmService
{
    private readonly ILlmClient _llm;
    private readonly JobConfiguration _config;
    private readonly ILogger<ConsultingFirmService> _logger;

    private static readonly ActivitySource ActivitySource =
        new("ResearchHarness.Agents.ConsultingFirm", "1.0.0");

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
        using var activity = ActivitySource.StartActivity("GetDomainBriefing", ActivityKind.Internal);
        activity?.SetTag("theme", theme);

        LogDomainBriefingStarted(_logger, theme);

        var request = new LlmRequest(
            Model: _config.LeadModel,
            SystemPrompt: ConsultingBriefingPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(ConsultingBriefingPrompt.BuildUserMessage(theme, uncertaintyContext))]
        );

        var response = await _llm.CompleteAsync(request, ct);
        return response.Content;
    }
    [LoggerMessage(2005, LogLevel.Information, "Consulting firm generating domain briefing for theme: {Theme}")]
    private static partial void LogDomainBriefingStarted(ILogger logger, string theme);
}
