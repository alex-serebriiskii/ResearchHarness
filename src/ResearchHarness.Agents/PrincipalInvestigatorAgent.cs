using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ResearchHarness.Agents.Internal;
using ResearchHarness.Agents.Prompts;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Agents;

public class PrincipalInvestigatorAgent : IPrincipalInvestigatorAgent
{
    private readonly ILlmClient _llm;
    private readonly ILabAgentServiceInternal _labAgent;
    private readonly ILogger<PrincipalInvestigatorAgent> _logger;

    public PrincipalInvestigatorAgent(
        ILlmClient llm,
        ILabAgentServiceInternal labAgent,
        ILogger<PrincipalInvestigatorAgent> logger)
    {
        _llm = llm;
        _labAgent = labAgent;
        _logger = logger;
    }

    public async Task<Paper> ResearchTopicAsync(
        ResearchTopic topic,
        JobConfiguration config,
        CancellationToken ct = default)
    {
        // Step 1: Break topic into search tasks
        var breakdownRequest = new LlmRequest(
            Model: config.PIModel,
            SystemPrompt: PITaskBreakdownPrompt.BuildSystemPrompt(),
            Messages: [LlmMessage.User(PITaskBreakdownPrompt.BuildUserMessage(topic, config.MaxLabAgentsPerPI))],
            OutputSchema: PITaskBreakdownPrompt.BuildOutputSchema()
        );
        var breakdownResponse = await _llm.CompleteAsync<TaskBreakdownOutput>(breakdownRequest, ct);

        var searchTasks = breakdownResponse.Content.Tasks
            .Take(config.MaxLabAgentsPerPI)
            .Select(t => new SearchTask(
                t.Query,
                t.TargetSourceTypes,
                t.ExtractionInstructions,
                t.RelevanceCriteria,
                t.FetchPageContent))
            .ToList();

        _logger.LogInformation(
            "PI created {Count} search tasks for topic {TopicId}",
            searchTasks.Count, topic.TopicId);

        // Step 2: Execute search tasks sequentially (Phase 1)
        var allFindings = new List<Finding>();
        var sourceMap = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchTask in searchTasks)
        {
            var result = await _labAgent.ExecuteSearchTaskFullAsync(searchTask, config, ct);
            allFindings.AddRange(result.Findings);
            foreach (var source in result.Sources)
                sourceMap.TryAdd(source.Url, source);
        }

        // Step 3: Synthesize findings into a paper
        var synthesisRequest = new LlmRequest(
            Model: config.PIModel,
            SystemPrompt: "You are a Principal Investigator synthesizing research findings into a structured paper. Be precise, evidence-based, and academically rigorous.",
            Messages: [LlmMessage.User(BuildSynthesisMessage(topic, allFindings, sourceMap.Values))],
            OutputSchema: BuildSynthesisSchema()
        );
        var synthesisResponse = await _llm.CompleteAsync<PaperSynthesisOutput>(synthesisRequest, ct);

        return new Paper(
            TopicId: topic.TopicId,
            ExecutiveSummary: synthesisResponse.Content.ExecutiveSummary,
            Findings: allFindings,
            Bibliography: [.. sourceMap.Values],
            ConfidenceScore: synthesisResponse.Content.ConfidenceScore,
            RevisionCount: 0,
            Reviews: []);
    }

    private static string BuildSynthesisMessage(
        ResearchTopic topic,
        IEnumerable<Finding> findings,
        IEnumerable<Source> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Research Topic: {topic.Title}");
        sb.AppendLine($"Scope: {topic.Scope}");
        sb.AppendLine();

        sb.AppendLine("Findings:");
        int i = 1;
        foreach (var finding in findings)
        {
            sb.AppendLine($"{i}. [{finding.SubTopic}] {finding.Summary} (Relevance: {finding.RelevanceScore:F2})");
            foreach (var kp in finding.KeyPoints)
                sb.AppendLine($"   - {kp}");
            i++;
        }

        sb.AppendLine();
        sb.AppendLine("Sources:");
        foreach (var source in sources)
            sb.AppendLine($"- {source.Title} ({source.Url}) [{source.Credibility}]");

        sb.AppendLine();
        sb.Append("Synthesize the above into: " +
                  "(1) An executive summary of the research. " +
                  "(2) A confidence score (0.0–1.0) reflecting certainty of conclusions " +
                  "given source quality and finding consistency.");

        return sb.ToString();
    }

    private static JsonObject BuildSynthesisSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("executive_summary", "confidence_score"),
            ["properties"] = new JsonObject
            {
                ["executive_summary"] = new JsonObject { ["type"] = "string" },
                ["confidence_score"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 1
                }
            }
        };
}
