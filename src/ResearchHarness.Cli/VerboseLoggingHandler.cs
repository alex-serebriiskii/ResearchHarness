using System.Diagnostics;

namespace ResearchHarness.Cli;

public sealed class VerboseLoggingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"[verbose] {request.Method} {request.RequestUri}");
        var sw = Stopwatch.StartNew();
        var response = await base.SendAsync(request, cancellationToken);
        sw.Stop();
        Console.Error.WriteLine(
            $"[verbose] {(int)response.StatusCode} {response.StatusCode} ({sw.ElapsedMilliseconds}ms)");
        return response;
    }
}
