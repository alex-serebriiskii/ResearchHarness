namespace ResearchHarness.Infrastructure.Llm;

public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Version { get; set; } = "2023-06-01";
    public int MaxConcurrentLlmCalls { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
}
