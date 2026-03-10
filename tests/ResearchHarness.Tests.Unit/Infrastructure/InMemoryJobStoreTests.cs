using AwesomeAssertions;
using ResearchHarness.Core;
using ResearchHarness.Core.Models;


namespace ResearchHarness.Tests.Unit.Infrastructure;

public class InMemoryJobStoreTests
{
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

    [Test]
    public async Task SaveAsync_ThenGetAsync_ReturnsJob()
    {
        var store = new InMemoryJobStore();
        var job = BuildJob();

        await store.SaveAsync(job);
        var retrieved = await store.GetAsync(job.JobId);

        retrieved.Should().NotBeNull();
        retrieved!.JobId.Should().Be(job.JobId);
        retrieved.Theme.Should().Be(job.Theme);
    }

    [Test]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var store = new InMemoryJobStore();
        var result = await store.GetAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Test]
    public async Task GetStatusAsync_KnownId_ReturnsStatus()
    {
        var store = new InMemoryJobStore();
        var job = BuildJob();
        await store.SaveAsync(job);

        var status = await store.GetStatusAsync(job.JobId);

        status.Should().Be(JobStatus.Pending);
    }

    [Test]
    public async Task GetStatusAsync_UnknownId_ReturnsNull()
    {
        var store = new InMemoryJobStore();
        var status = await store.GetStatusAsync(Guid.NewGuid());
        status.Should().BeNull();
    }

    [Test]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        var store = new InMemoryJobStore();
        var job = BuildJob();
        await store.SaveAsync(job);

        await store.UpdateStatusAsync(job.JobId, JobStatus.Researching);

        var status = await store.GetStatusAsync(job.JobId);
        status.Should().Be(JobStatus.Researching);
    }

    [Test]
    public async Task UpdateStatusAsync_UnknownId_DoesNotThrow()
    {
        var store = new InMemoryJobStore();
        // Should silently no-op rather than throw
        Func<Task> act = () => store.UpdateStatusAsync(Guid.NewGuid(), JobStatus.Failed);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task SaveAsync_Overwrite_ReplacesExistingJob()
    {
        var store = new InMemoryJobStore();
        var id = Guid.NewGuid();
        var original = BuildJob(id);
        var updated = original with { Status = JobStatus.Completed };

        await store.SaveAsync(original);
        await store.SaveAsync(updated);

        var retrieved = await store.GetAsync(id);
        retrieved!.Status.Should().Be(JobStatus.Completed);
    }

    [Test]
    public async Task GetJournalAsync_NoResult_ReturnsNull()
    {
        var store = new InMemoryJobStore();
        var job = BuildJob();
        await store.SaveAsync(job);

        var journal = await store.GetJournalAsync(job.JobId);
        journal.Should().BeNull();
    }

    [Test]
    public async Task GetJournalAsync_WithResult_ReturnsJournal()
    {
        var store = new InMemoryJobStore();
        var id = Guid.NewGuid();
        var journal = new Journal(
            "summary", "analysis", [], [], DateTimeOffset.UtcNow);
        var job = BuildJob(id) with { Result = journal, Status = JobStatus.Completed };

        await store.SaveAsync(job);

        var retrieved = await store.GetJournalAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.OverallSummary.Should().Be("summary");
    }

    [Test]
    public async Task ConcurrentWrites_AreThreadSafe()
    {
        var store = new InMemoryJobStore();
        var jobs = Enumerable.Range(0, 50).Select(_ => BuildJob()).ToList();

        // Write all concurrently
        await Task.WhenAll(jobs.Select(j => store.SaveAsync(j)));

        // All should be retrievable
        foreach (var job in jobs)
        {
            var retrieved = await store.GetAsync(job.JobId);
            retrieved.Should().NotBeNull();
        }
    }
}
