namespace ResearchHarness.Infrastructure.Llm;

public class LlmException : Exception
{
    public int? StatusCode { get; }
    public string? RawResponse { get; }

    public LlmException(string message) : base(message) { }

    public LlmException(string message, int statusCode, string rawResponse)
        : base(message)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;
    }
}
