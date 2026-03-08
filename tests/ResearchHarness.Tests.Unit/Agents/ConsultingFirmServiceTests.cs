using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ResearchHarness.Agents;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Llm;

namespace ResearchHarness.Tests.Unit.Agents;

public class ConsultingFirmServiceTests
{
    private ILlmClient _llm = null!;
    private ConsultingFirmService _service = null!;
    private JobConfiguration _config = null!;

    [Before(Test)]
    public void Setup()
    {
        _llm = Substitute.For<ILlmClient>();
        _config = new JobConfiguration(LeadModel: "claude-lead-test");
        _service = new ConsultingFirmService(
            _llm, _config, Substitute.For<ILogger<ConsultingFirmService>>());
    }

    [Test]
    public async Task GetDomainBriefingAsync_ReturnsBriefing()
    {
        const string expected = "Domain briefing content here.";
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<string>(expected, new TokenUsage(100, 200), "end_turn"));

        var result = await _service.GetDomainBriefingAsync("AI 2025", "uncertain about models");

        result.Should().Be(expected);
    }

    [Test]
    public async Task GetDomainBriefingAsync_UsesLeadModel()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<string>("briefing", new TokenUsage(10, 20), "end_turn"));

        await _service.GetDomainBriefingAsync("theme", "context");

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.Model == _config.LeadModel),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetDomainBriefingAsync_PassesThemeAndUncertaintyInMessage()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<string>("briefing", new TokenUsage(10, 20), "end_turn"));

        const string theme = "nanotechnology 2025";
        const string uncertainty = "unclear regulation";
        await _service.GetDomainBriefingAsync(theme, uncertainty);

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r =>
                r.Messages.Any(m => m.Content.Contains(theme)) &&
                r.Messages.Any(m => m.Content.Contains(uncertainty))),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetDomainBriefingAsync_NoOutputSchema()
    {
        _llm.CompleteAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LlmResponse<string>("briefing", new TokenUsage(10, 20), "end_turn"));

        await _service.GetDomainBriefingAsync("theme", "context");

        await _llm.Received(1).CompleteAsync(
            Arg.Is<LlmRequest>(r => r.OutputSchema == null),
            Arg.Any<CancellationToken>());
    }
}
