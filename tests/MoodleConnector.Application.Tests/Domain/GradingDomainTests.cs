using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Domain;

public sealed class GradingDomainTests
{
    [Fact]
    public void AssistedGradingBatch_Create_IniciaContadoresEStatus()
    {
        var batch = AssistedGradingBatch.Create(
            courseId: 10,
            assignmentIds: [501, 502],
            createdBySubject: "teacher-1",
            createdByMoodleUserId: 321,
            totalItems: 25);

        Assert.NotEqual(Guid.Empty, batch.Id);
        Assert.Equal(10, batch.CourseId);
        Assert.Equal([501, 502], batch.AssignmentIds);
        Assert.Equal("teacher-1", batch.CreatedBySubject);
        Assert.Equal(GradingBatchStatus.Pending, batch.Status);
        Assert.Equal(25, batch.TotalItems);
        Assert.Equal(0, batch.ProcessedItems);
        Assert.Equal(0, batch.ReadyItems);
        Assert.Equal(0, batch.BlockedItems);
        Assert.Equal(0, batch.FailedItems);
    }

    [Fact]
    public void AssistedGradingItem_ApplyTeacherReview_SeparaNotaSugeridaDeNotaFinal()
    {
        var item = AssistedGradingItem.Create(
            batchId: Guid.NewGuid(),
            courseId: 10,
            assignmentId: 501,
            submissionId: 9001,
            moodleUserId: 101,
            attemptNumber: 0);

        item.SetDraft(
            suggestedGrade: 7.5m,
            confidence: 0.82m,
            draftFeedback: "Feedback sugerido.",
            privateNotesToTeacher: "Observacao privada do rascunho.");

        item.ApplyTeacherReview(
            finalGrade: 8.0m,
            finalFeedback: "Feedback revisado pelo professor.",
            reviewedBySubject: "teacher-1",
            reviewedByMoodleUserId: 321);

        Assert.Equal(7.5m, item.SuggestedGrade);
        Assert.Equal("Observacao privada do rascunho.", item.PrivateNotesToTeacher);
        Assert.Equal(8.0m, item.FinalGrade);
        Assert.Equal("Feedback revisado pelo professor.", item.FinalFeedback);
        Assert.Equal(GradingReviewStatus.Reviewed, item.ReviewStatus);
        Assert.Equal(GradingItemStatus.ReadyToCommit, item.Status);
        Assert.Equal(GradingCommitStatus.Pending, item.CommitStatus);
        Assert.Equal("teacher-1", item.ReviewedBySubject);
        Assert.NotNull(item.ReviewedAt);
    }

    [Fact]
    public void AssistedGradingItem_MarkAnalysisFailed_MarcaFalhaSemCommitMoodle()
    {
        var item = AssistedGradingItem.Create(
            batchId: Guid.NewGuid(),
            courseId: 10,
            assignmentId: 501,
            submissionId: 9001,
            moodleUserId: 101,
            attemptNumber: 0);

        item.MarkAnalysisFailed("Erro ao montar contexto.");

        Assert.Equal(GradingItemStatus.Failed, item.Status);
        Assert.Equal(GradingCommitStatus.NotReady, item.CommitStatus);
        Assert.Equal(0m, item.Confidence);
        Assert.Contains("Erro ao montar contexto", item.DraftFeedback);
        Assert.Equal(item.DraftFeedback, item.PrivateNotesToTeacher);
    }

    [Fact]
    public void GradingContext_HasMinimumContext_FicaFalseQuandoHaBloqueadoresMesmoComArquivo()
    {
        var context = GradingContext.Build(
            gradingItemId: Guid.NewGuid(),
            batchId: Guid.NewGuid(),
            courseId: "10",
            assignmentId: "501",
            submissionId: "9001",
            studentId: "101",
            assignmentStatement: null,
            criteria: null,
            rubricDescription: null,
            maxGrade: null,
            gradeScale: null,
            submissionText: null,
            attachedFiles:
            [
                new GradingFileInfo(
                    "entrega.pdf",
                    "application/pdf",
                    FileSizeBytes: 1200,
                    Sha256: "hash-1",
                    ExtractedText: "Texto extraido previamente.",
                    IsSupported: true)
            ],
            courseMaterials: null,
            teacherInstructions: null);

        Assert.NotEmpty(context.Blockers);
        Assert.False(context.HasMinimumContext);
    }
}
