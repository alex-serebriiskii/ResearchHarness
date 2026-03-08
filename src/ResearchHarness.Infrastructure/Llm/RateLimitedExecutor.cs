namespace ResearchHarness.Infrastructure.Llm;

public sealed class RateLimitedExecutor : IDisposable
{
    private readonly SemaphoreSlim _llmSemaphore;
    private readonly SemaphoreSlim _searchSemaphore;

    public RateLimitedExecutor(int maxLlmConcurrency = 10, int maxSearchConcurrency = 5)
    {
        _llmSemaphore = new SemaphoreSlim(maxLlmConcurrency, maxLlmConcurrency);
        _searchSemaphore = new SemaphoreSlim(maxSearchConcurrency, maxSearchConcurrency);
    }

    public async Task<T> ExecuteLlmCallAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        await _llmSemaphore.WaitAsync(ct);
        try
        {
            return await call();
        }
        finally
        {
            _llmSemaphore.Release();
        }
    }

    public async Task<T> ExecuteSearchCallAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        await _searchSemaphore.WaitAsync(ct);
        try
        {
            return await call();
        }
        finally
        {
            _searchSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _llmSemaphore.Dispose();
        _searchSemaphore.Dispose();
    }
}
