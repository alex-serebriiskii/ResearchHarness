using System.Text.Json;

namespace ResearchHarness.Cli.Configuration;

public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CliConfiguration Load(
        string? serverArg,
        string? apiKeyArg,
        string? formatArg,
        int? timeoutArg,
        int? pollIntervalArg,
        bool noProgress,
        bool verbose,
        string? outputFile)
    {
        var file = LoadFromFile();

        var server = serverArg
            ?? Environment.GetEnvironmentVariable("RESEARCH_HARNESS_URL")
            ?? file?.Server
            ?? "http://localhost:5000";

        var apiKey = apiKeyArg
            ?? Environment.GetEnvironmentVariable("RESEARCH_HARNESS_API_KEY")
            ?? file?.ApiKey;

        var format = formatArg
            ?? file?.DefaultFormat
            ?? (Console.IsOutputRedirected && outputFile is null ? "json" : "markdown");

        var timeout = timeoutArg
            ?? file?.DefaultTimeout
            ?? 1800;

        var pollInterval = pollIntervalArg
            ?? file?.DefaultPollInterval
            ?? 10;

        return new CliConfiguration(server, apiKey, format, timeout, pollInterval, noProgress, verbose, outputFile);
    }

    private static ConfigFile? LoadFromFile()
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse config file {path}: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Warning: Failed to read config file {path}: {ex.Message}");
            return null;
        }
    }

    private static string GetConfigFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "research-harness", "config.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "research-harness", "config.json");
    }

    private sealed record ConfigFile(
        string? Server,
        string? ApiKey,
        string? DefaultFormat,
        int? DefaultTimeout,
        int? DefaultPollInterval);
}
