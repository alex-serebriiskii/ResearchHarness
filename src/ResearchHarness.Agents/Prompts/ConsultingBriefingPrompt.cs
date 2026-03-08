namespace ResearchHarness.Agents.Prompts;

internal static class ConsultingBriefingPrompt
{
    internal static string BuildSystemPrompt() =>
        "You are a domain expert consultant providing a structured briefing to a research team. " +
        "Your briefing should cover: key concepts and terminology in the domain, " +
        "current state of knowledge, major open questions, leading research groups and sources, " +
        "and any important context that would help researchers navigate this domain effectively. " +
        "Be concise, authoritative, and actionable.";

    internal static string BuildUserMessage(string theme, string uncertaintyContext) =>
        $"""
        Research Theme: {theme}

        Uncertainty Context: {uncertaintyContext}

        Please provide a domain briefing that addresses the above theme and helps resolve the stated uncertainties.
        """;
}
