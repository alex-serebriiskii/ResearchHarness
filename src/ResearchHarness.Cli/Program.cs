using System.CommandLine;
using ResearchHarness.Cli.Commands;
using ResearchHarness.Cli.Configuration;
using ResearchHarness.Core.Models;

// ── Global options ────────────────────────────────────────────────────────────

var serverOption = new Option<string?>("--server", "-s")
{
    Description = "Server URL (default: http://localhost:5000, env: RESEARCH_HARNESS_URL)"
};

var apiKeyOption = new Option<string?>("--api-key", "-k")
{
    Description = "API key (env: RESEARCH_HARNESS_API_KEY)"
};

var formatOption = new Option<string?>("--format", "-f")
{
    Description = "Output format: markdown, json, text (default: auto-detect)"
};

var timeoutOption = new Option<int?>("--timeout")
{
    Description = "Max wait time in seconds for the run command (default: 1800)"
};

var pollIntervalOption = new Option<int?>("--poll-interval")
{
    Description = "Status poll interval in seconds (default: 10)"
};

var noProgressOption = new Option<bool>("--no-progress")
{
    Description = "Suppress progress output on stderr"
};

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Show HTTP request/response details on stderr"
};

var outputOption = new Option<string?>("--output", "-o")
{
    Description = "Write primary output to a file instead of stdout"
};

// ── Helper to build CliConfiguration from parsed values ────���──────────────────

CliConfiguration BuildConfig(ParseResult pr) =>
    ConfigurationLoader.Load(
        pr.GetValue(serverOption),
        pr.GetValue(apiKeyOption),
        pr.GetValue(formatOption),
        pr.GetValue(timeoutOption),
        pr.GetValue(pollIntervalOption),
        pr.GetValue(noProgressOption),
        pr.GetValue(verboseOption),
        pr.GetValue(outputOption));

// Shared set of global options to add to commands
Option[] globalOptions = [serverOption, apiKeyOption, formatOption, timeoutOption, pollIntervalOption, noProgressOption, verboseOption, outputOption];

void AddGlobalOptions(Command command, params Option[] options)
{
    foreach (var option in options)
        command.Add(option);
}

// ── Root command (research-harness <theme>) ───────────────────────────────────

var themeArgument = new Argument<string?>("theme")
{
    Description = "Research theme — submits a job, waits for completion, and prints the journal",
    Arity = ArgumentArity.ZeroOrOne
};

var rootCommand = new RootCommand("ResearchHarness CLI — deep research as a single command");
rootCommand.Add(themeArgument);
AddGlobalOptions(rootCommand, globalOptions);

rootCommand.SetAction(async (pr, ct) =>
{
    var theme = pr.GetValue(themeArgument);
    if (string.IsNullOrWhiteSpace(theme))
    {
        Console.Error.WriteLine("Provide a research theme, or use --help for usage.");
        return 2;
    }

    return await RunCommand.ExecuteAsync(theme, BuildConfig(pr), ct);
});

// ── run command (explicit alias) ────────���─────────────────────────────────────

var runThemeArg = new Argument<string>("theme") { Description = "Research theme to investigate" };
var runCommand = new Command("run", "Submit a research job, wait for completion, and print the journal");
runCommand.Add(runThemeArg);
AddGlobalOptions(runCommand, globalOptions);

runCommand.SetAction(async (pr, ct) =>
    await RunCommand.ExecuteAsync(pr.GetValue(runThemeArg)!, BuildConfig(pr), ct));

rootCommand.Add(runCommand);

// ── submit command ───────────���────────────────────────────────────���───────────

var submitThemeArg = new Argument<string>("theme") { Description = "Research theme to submit" };
var submitCommand = new Command("submit", "Submit a research job and print the job ID");
submitCommand.Add(submitThemeArg);
AddGlobalOptions(submitCommand, serverOption, apiKeyOption, noProgressOption, verboseOption);

submitCommand.SetAction(async (pr, ct) =>
    await SubmitCommand.ExecuteAsync(pr.GetValue(submitThemeArg)!, BuildConfig(pr), ct));

rootCommand.Add(submitCommand);

// ── status command ───────────────���────────────────────────────────────────────

var statusJobIdArg = new Argument<Guid>("jobId") { Description = "Job ID to check" };
var statusCommand = new Command("status", "Get the current status of a research job");
statusCommand.Add(statusJobIdArg);
AddGlobalOptions(statusCommand, serverOption, apiKeyOption, verboseOption);

statusCommand.SetAction(async (pr, ct) =>
    await StatusCommand.ExecuteAsync(pr.GetValue(statusJobIdArg), BuildConfig(pr), ct));

rootCommand.Add(statusCommand);

// ── journal command ───────���───────────────────────────────────────────────────

var journalJobIdArg = new Argument<Guid>("jobId") { Description = "Job ID to retrieve the journal for" };
var journalCommand = new Command("journal", "Get the completed journal for a research job");
journalCommand.Add(journalJobIdArg);
AddGlobalOptions(journalCommand, serverOption, apiKeyOption, formatOption, verboseOption, outputOption);

journalCommand.SetAction(async (pr, ct) =>
    await JournalCommand.ExecuteAsync(pr.GetValue(journalJobIdArg), BuildConfig(pr), ct));

rootCommand.Add(journalCommand);

// ── cost command ───────────────��──────────────────────────────────────────────

var costJobIdArg = new Argument<Guid>("jobId") { Description = "Job ID to retrieve cost summary for" };
var costCommand = new Command("cost", "Get the token usage cost summary for a research job");
costCommand.Add(costJobIdArg);
AddGlobalOptions(costCommand, serverOption, apiKeyOption, formatOption, verboseOption, outputOption);

costCommand.SetAction(async (pr, ct) =>
    await CostCommand.ExecuteAsync(pr.GetValue(costJobIdArg), BuildConfig(pr), ct));

rootCommand.Add(costCommand);

// ── cancel command ─────────────────────────────────────────────��──────────────

var cancelJobIdArg = new Argument<Guid>("jobId") { Description = "Job ID to cancel" };
var cancelCommand = new Command("cancel", "Cancel an in-flight research job");
cancelCommand.Add(cancelJobIdArg);
AddGlobalOptions(cancelCommand, serverOption, apiKeyOption, verboseOption);

cancelCommand.SetAction(async (pr, ct) =>
    await CancelCommand.ExecuteAsync(pr.GetValue(cancelJobIdArg), BuildConfig(pr), ct));

rootCommand.Add(cancelCommand);

// ── watch command ───────────────���────────────────────────────────���────────────

var watchJobIdArg = new Argument<Guid>("jobId") { Description = "Job ID to watch" };
var watchCommand = new Command("watch", "Attach to a running job, poll until complete, and print the journal");
watchCommand.Add(watchJobIdArg);
AddGlobalOptions(watchCommand, globalOptions);

watchCommand.SetAction(async (pr, ct) =>
    await WatchCommand.ExecuteAsync(pr.GetValue(watchJobIdArg), BuildConfig(pr), ct));

rootCommand.Add(watchCommand);

// ── list command ─────��────────────────────────────────────���───────────────────

var listStatusOption = new Option<string?>("--status") { Description = "Filter by job status" };
var listOffsetOption = new Option<int>("--offset") { Description = "Pagination offset", DefaultValueFactory = _ => 0 };
var listLimitOption = new Option<int>("--limit") { Description = "Maximum jobs to return", DefaultValueFactory = _ => 20 };

var listCommand = new Command("list", "List research jobs");
listCommand.Add(listStatusOption);
listCommand.Add(listOffsetOption);
listCommand.Add(listLimitOption);
AddGlobalOptions(listCommand, serverOption, apiKeyOption, formatOption, verboseOption, outputOption);

listCommand.SetAction(async (pr, ct) =>
{
    var statusStr = pr.GetValue(listStatusOption);
    JobStatus? status = null;
    if (statusStr is not null)
    {
        if (!Enum.TryParse<JobStatus>(statusStr, ignoreCase: true, out var parsed))
        {
            var valid = string.Join(", ", Enum.GetNames<JobStatus>());
            Console.Error.WriteLine($"Error: Unknown status '{statusStr}'. Valid values: {valid}");
            return 2;
        }
        status = parsed;
    }

    return await ListCommand.ExecuteAsync(
        status,
        pr.GetValue(listOffsetOption),
        pr.GetValue(listLimitOption),
        BuildConfig(pr),
        ct);
});

rootCommand.Add(listCommand);

// ── Run ─────────��─────────────────────────────────���───────────────────────────

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
