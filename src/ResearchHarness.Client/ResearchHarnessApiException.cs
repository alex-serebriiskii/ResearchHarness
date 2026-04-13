using System.Net;

namespace ResearchHarness.Client;

/// <summary>
/// Base exception for ResearchHarness API errors.
/// </summary>
public class ResearchHarnessApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public ResearchHarnessApiException(HttpStatusCode statusCode, string? responseBody = null)
        : base($"API request failed with status {(int)statusCode} ({statusCode}). {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

public class JobNotFoundException : ResearchHarnessApiException
{
    public Guid JobId { get; }

    public JobNotFoundException(Guid jobId, string? responseBody = null)
        : base(HttpStatusCode.NotFound, responseBody ?? $"Job {jobId} not found.")
    {
        JobId = jobId;
    }
}

public class JobNotReadyException : ResearchHarnessApiException
{
    public Guid JobId { get; }

    public JobNotReadyException(Guid jobId, string? responseBody = null)
        : base(HttpStatusCode.Conflict, responseBody ?? $"Job {jobId} is not yet completed.")
    {
        JobId = jobId;
    }
}
