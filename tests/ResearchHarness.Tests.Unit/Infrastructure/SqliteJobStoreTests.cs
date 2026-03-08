using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using ResearchHarness.Core;
using ResearchHarness.Core.Models;
using ResearchHarness.Infrastructure.Persistence;

namespace ResearchHarness.Tests.Unit.Infrastructure;

public class SqliteJobStoreTests : IAsyncDisposable
{
    private SqliteConnection _referenceConn = null!;
    private SqliteJobStore _store = null!;

    private static ResearchJob BuildJob(Guid? id = null) =>
        new(
            JobId: id ?? Guid.NewGuid(),
            Theme: "test theme",
            DomainContext: null,
            Status: JobStatus.Pending,
            Topics: [],
            Result: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            Config: new JobConfiguration()
        );

    [Before(Test)]
    public async Task Setup()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _referenceConn = new SqliteConnection(connectionString);
        await _referenceConn.OpenAsync(); // Keep DB alive across per-operation connections
        _store = new SqliteJobStore(connectionString);
    }

    [After(Test)]
    public async ValueTask DisposeAsync()
    {
        await _referenceConn.CloseAsync();
        await _referenceConn.DisposeAsync();
    }

    [Test]
    public async Task SaveAsync_ThenGetAsync_ReturnsJob()
    {
        var job = BuildJob();
        await _store.SaveAsync(job);
        var retrieved = await _store.GetAsync(job.JobId);
        retrieved.Should().NotBeNull();
        retrieved!.JobId.Should().Be(job.JobId);
        retrieved.Theme.Should().Be(job.Theme);
    }

    [Test]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var result = await _store.GetAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Test]
    public async Task GetStatusAsync_KnownId_ReturnsStatus()
    {
        var job = BuildJob();
        await _store.SaveAsync(job);
        var status = await _store.GetStatusAsync(job.JobId);
        status.Should().Be(JobStatus.Pending);
    }

    [Test]
    public async Task GetStatusAsync_UnknownId_ReturnsNull()
    {
        var status = await _store.GetStatusAsync(Guid.NewGuid());
        status.Should().BeNull();
    }

    [Test]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        var job = BuildJob();
        await _store.SaveAsync(job);
        await _store.UpdateStatusAsync(job.JobId, JobStatus.Researching);
        var status = await _store.GetStatusAsync(job.JobId);
        status.Should().Be(JobStatus.Researching);
    }

    [Test]
    public async Task UpdateStatusAsync_UnknownId_DoesNotThrow()
    {
        Func<Task> act = () => _store.UpdateStatusAsync(Guid.NewGuid(), JobStatus.Failed);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SaveAsync_Overwrite_ReplacesExistingJob()
    {
        var id = Guid.NewGuid();
        var original = BuildJob(id);
        var updated = original with { Status = JobStatus.Completed };

        await _store.SaveAsync(original);
        await _store.SaveAsync(updated);

        var retrieved = await _store.GetAsync(id);
        retrieved!.Status.Should().Be(JobStatus.Completed);
    }

    [Test]
    public async Task GetJournalAsync_NoResult_ReturnsNull()
    {
        var job = BuildJob();
        await _store.SaveAsync(job);
        var journal = await _store.GetJournalAsync(job.JobId);
        journal.Should().BeNull();
    }

    [Test]
    public async Task GetJournalAsync_WithResult_ReturnsJournal()
    {
        var id = Guid.NewGuid();
        var journal = new Journal("summary", "analysis", [], [], DateTimeOffset.UtcNow);
        var job = BuildJob(id) with { Result = journal, Status = JobStatus.Completed };

        await _store.SaveAsync(job);

        var retrieved = await _store.GetJournalAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.OverallSummary.Should().Be("summary");
    }
}
