namespace ResearchHarness.Orchestration;

public sealed class ResearchScheduleOptions
{
    public List<ScheduledResearchEntry> Schedule { get; set; } = [];
}

public sealed class ScheduledResearchEntry
{
    public string Theme { get; set; } = "";
    public string CronExpression { get; set; } = "";
}
