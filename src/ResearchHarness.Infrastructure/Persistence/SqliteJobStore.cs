using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ResearchHarness.Core;
using ResearchHarness.Core.Interfaces;
using ResearchHarness.Core.Models;

namespace ResearchHarness.Infrastructure.Persistence;

/// <summary>
/// IJobStore backed by SQLite. Jobs are stored as JSON blobs — schema:
/// Jobs(Id TEXT PK, Status TEXT, CreatedAt TEXT, CompletedAt TEXT, DataJson TEXT).
/// The table is created on first access; no manual migration required.
/// </summary>
public sealed class SqliteJobStore : IJobStore
{
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(),
            new TimeSpanConverter()
        }
    };

    public SqliteJobStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task SaveAsync(ResearchJob job, CancellationToken ct = default)
    {
        var dataJson = JsonSerializer.Serialize(job, SerializerOptions);
        await using var conn = await OpenAsync(ct);
        await EnsureTableAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Jobs (Id, Status, CreatedAt, CompletedAt, DataJson)
            VALUES ($id, $status, $createdAt, $completedAt, $dataJson)
            """;
        cmd.Parameters.AddWithValue("$id", job.JobId.ToString());
        cmd.Parameters.AddWithValue("$status", job.Status.ToString());
        cmd.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$completedAt",
            job.CompletedAt.HasValue ? job.CompletedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$dataJson", dataJson);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ResearchJob?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await EnsureTableAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DataJson FROM Jobs WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", jobId.ToString());
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        return JsonSerializer.Deserialize<ResearchJob>(result.ToString()!, SerializerOptions);
    }

    public async Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await EnsureTableAsync(conn, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Status FROM Jobs WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", jobId.ToString());
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        return Enum.Parse<JobStatus>(result.ToString()!);
    }

    public async Task<Journal?> GetJournalAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await GetAsync(jobId, ct);
        return job?.Result;
    }

    public async Task UpdateStatusAsync(Guid jobId, JobStatus status, CancellationToken ct = default)
    {
        var job = await GetAsync(jobId, ct);
        if (job is null) return; // unknown job — no-op
        await SaveAsync(job with { Status = status }, ct);
    }

    public async Task<(IReadOnlyList<ResearchJob> Jobs, int Total)> ListJobsAsync(
        int offset = 0, int limit = 20, JobStatus? status = null, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await EnsureTableAsync(conn, ct);

        await using var countCmd = conn.CreateCommand();
        if (status.HasValue)
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Status = $status";
            countCmd.Parameters.AddWithValue("$status", status.Value.ToString());
        }
        else
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Jobs";
        }
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        await using var dataCmd = conn.CreateCommand();
        if (status.HasValue)
        {
            dataCmd.CommandText = "SELECT DataJson FROM Jobs WHERE Status = $status ORDER BY CreatedAt DESC LIMIT $limit OFFSET $offset";
            dataCmd.Parameters.AddWithValue("$status", status.Value.ToString());
        }
        else
        {
            dataCmd.CommandText = "SELECT DataJson FROM Jobs ORDER BY CreatedAt DESC LIMIT $limit OFFSET $offset";
        }
        dataCmd.Parameters.AddWithValue("$limit", limit);
        dataCmd.Parameters.AddWithValue("$offset", offset);

        var jobs = new List<ResearchJob>();
        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(0);
            var job = JsonSerializer.Deserialize<ResearchJob>(json, SerializerOptions);
            if (job is not null)
                jobs.Add(job);
        }

        return (jobs, total);
    }

    public async Task<JobCostSummary?> GetCostAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await GetAsync(jobId, ct);
        return job?.CostSummary;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task EnsureTableAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Jobs (
                Id TEXT PRIMARY KEY,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                CompletedAt TEXT,
                DataJson TEXT NOT NULL
            )
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // STJ has no built-in TimeSpan converter; this one uses the standard "c" format ("00:30:00").
    private sealed class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => TimeSpan.Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
