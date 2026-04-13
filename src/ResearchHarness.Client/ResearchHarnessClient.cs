using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Client;

/// <summary>
/// Typed HTTP client for the ResearchHarness API.
/// Wraps all /internal/research endpoints.
/// </summary>
public sealed class ResearchHarnessClient : IResearchHarnessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public ResearchHarnessClient(
        string baseUrl,
        string? apiKey = null,
        TimeSpan? httpTimeout = null,
        HttpMessageHandler? innerHandler = null)
    {
        _http = innerHandler is not null
            ? new HttpClient(innerHandler, disposeHandler: true)
            : new HttpClient();
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = httpTimeout ?? TimeSpan.FromSeconds(30);
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _ownsHttpClient = true;
    }

    public ResearchHarnessClient(HttpClient httpClient)
    {
        _http = httpClient;
        _ownsHttpClient = false;
    }

    public async Task<Guid> StartJobAsync(string theme, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "internal/research/start",
            new { theme },
            JsonOptions,
            ct);

        await ThrowOnErrorAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions, ct);
    }

    public async Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"internal/research/{jobId}/status", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        await ThrowOnErrorAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JobStatus>(JsonOptions, ct);
    }

    public async Task<Journal> GetJournalAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"internal/research/{jobId}/journal", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new JobNotFoundException(jobId);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new JobNotReadyException(jobId, body);
        }

        await ThrowOnErrorAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<Journal>(JsonOptions, ct))!;
    }

    public async Task<JobCostSummary> GetCostAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"internal/research/{jobId}/cost", ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new JobNotFoundException(jobId);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new JobNotReadyException(jobId, body);
        }

        await ThrowOnErrorAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<JobCostSummary>(JsonOptions, ct))!;
    }

    public async Task CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"internal/research/{jobId}/cancel", null, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new JobNotFoundException(jobId);

        // 409 is expected if job is already completed/failed — don't throw
        if (response.StatusCode == HttpStatusCode.Conflict)
            return;

        await ThrowOnErrorAsync(response, ct);
    }

    public async Task<JobListResult> ListJobsAsync(
        JobStatus? status = null,
        int offset = 0,
        int limit = 20,
        CancellationToken ct = default)
    {
        var queryParts = new List<string>
        {
            $"offset={offset}",
            $"limit={limit}"
        };
        if (status.HasValue)
            queryParts.Add($"status={Uri.EscapeDataString(status.Value.ToString())}");

        var url = $"internal/research/jobs?{string.Join("&", queryParts)}";

        var response = await _http.GetAsync(url, ct);
        await ThrowOnErrorAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<JobListResult>(JsonOptions, ct))!;
    }

    private static async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ResearchHarnessApiException(
                HttpStatusCode.Unauthorized,
                "Authentication failed. Check your API key.");

        throw new ResearchHarnessApiException(response.StatusCode, body);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
            _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
