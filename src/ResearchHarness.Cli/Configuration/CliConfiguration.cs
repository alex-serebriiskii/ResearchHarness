namespace ResearchHarness.Cli.Configuration;

public record CliConfiguration(
    string Server,
    string? ApiKey,
    string Format,
    int TimeoutSeconds,
    int PollIntervalSeconds,
    bool NoProgress,
    bool Verbose,
    string? OutputFile);
