using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingAuditQueryHandlerTests
{
    [Fact]
    public async Task Handle_RetornaEventosDeAuditoriaPorAuditId()
    {
        var repository = new FakeAuditLogRepository();
        repository.Logs.Add(new MoodleAuditLog
        {
            CorrelationId = "audit-1",
            BatchJobId = Guid.Parse("00000000-0000-0000-0000-000000000123"),
            ToolName = "confirmar_lancamento_lote_moodle",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = "teacher-1",
            ActorEmail = "teacher@example.com",
            ActorMoodleUserId = 321,
            CourseId = 10,
            MoodleFunction = "mod_assign_save_grade",
            RequestSanitizedJson = "{\"gradingItemId\":\"item-1\",\"assignmentId\":\"501\",\"studentId\":\"101\"}",
            ResponseSummaryJson = "{\"moodleStatus\":\"ok\"}",
            Status = "commit_succeeded",
            CreatedAt = new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero)
        });
        var sut = new GetGradingAuditQueryHandler(repository);

        var result = await sut.Handle(
            new GetGradingAuditQuery("audit-1", Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.Equal("audit-1", result.AuditId);
        Assert.Equal(1, result.TotalEvents);
        var auditEvent = Assert.Single(result.Events);
        Assert.Equal("commit_succeeded", auditEvent.Status);
        Assert.Equal("confirmar_lancamento_lote_moodle", auditEvent.ToolName);
        Assert.Equal("mod_assign_save_grade", auditEvent.MoodleFunction);
        Assert.Equal("501", auditEvent.Request.GetProperty("assignmentId").GetString());
        Assert.Equal("ok", auditEvent.Response.GetProperty("moodleStatus").GetString());
        Assert.DoesNotContain("token", auditEvent.Request.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleBatchAudit_RetornaEventosDeAuditoriaPorBatchJobId()
    {
        var batchId = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var repository = new FakeAuditLogRepository();
        repository.Logs.Add(new MoodleAuditLog
        {
            CorrelationId = "audit-2",
            BatchJobId = batchId,
            ToolName = "confirmar_lancamento_lote_moodle",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = "teacher-1",
            ActorEmail = "teacher@example.com",
            ActorMoodleUserId = 321,
            CourseId = 10,
            MoodleFunction = "mod_assign_save_grade",
            RequestSanitizedJson = "{\"gradingItemId\":\"item-1\",\"batchJobId\":\"00000000-0000-0000-0000-000000000123\"}",
            ResponseSummaryJson = "{\"moodleStatus\":\"ok\"}",
            Status = "commit_succeeded",
            CreatedAt = new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero)
        });
        var sut = new GetGradingBatchAuditQueryHandler(repository);

        var result = await sut.Handle(
            new GetGradingBatchAuditQuery(batchId, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.Null(result.AuditId);
        Assert.Equal(batchId, result.BatchJobId);
        Assert.Equal(1, result.TotalEvents);
        var auditEvent = Assert.Single(result.Events);
        Assert.Equal("commit_succeeded", auditEvent.Status);
    }

    private sealed class FakeAuditLogRepository : IMoodleAuditLogRepository
    {
        public List<MoodleAuditLog> Logs { get; } = [];

        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(
            string correlationId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.CorrelationId == correlationId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.CorrelationId == correlationId));
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(
            Guid batchJobId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.BatchJobId == batchJobId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.BatchJobId == batchJobId));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
