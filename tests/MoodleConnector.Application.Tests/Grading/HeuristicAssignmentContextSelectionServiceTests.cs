using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class HeuristicAssignmentContextSelectionServiceTests
{
    [Fact]
    public async Task SelectAsync_PriorizaDocumentoComSapEtapaEOrientacoesDaTarefa()
    {
        var sut = new HeuristicAssignmentContextSelectionService();

        var result = await sut.SelectAsync(
            new AssignmentContextSelectionRequest(
                CourseId: "29972",
                AssignmentId: "101112",
                AssignmentName: "Envio SAP 01 - Etapa 1",
                AssignmentDescription: null,
                Candidates:
                [
                    new AssignmentContextCandidate(
                        "c1",
                        "resource",
                        "Calendario do curso",
                        "Datas gerais e avisos administrativos.",
                        SectionNumber: 1,
                        DistanceFromAssignment: 1),
                    new AssignmentContextCandidate(
                        "c2",
                        "resource",
                        "Orientacoes SAP 01 - Etapa 1",
                        "Enunciado da atividade SAP 01 etapa 1 com criterios de entrega e itens esperados.",
                        SectionNumber: 1,
                        DistanceFromAssignment: 0),
                    new AssignmentContextCandidate(
                        "c3",
                        "page",
                        "Material complementar ITIL",
                        "Texto de apoio para estudo, sem instrucoes de envio.",
                        SectionNumber: 1,
                        DistanceFromAssignment: 2)
                ]),
            CancellationToken.None);

        Assert.Equal("c2", result.SelectedCandidateId);
        Assert.Equal("assignment_statement", result.Classification);
        Assert.True(result.Confidence >= 0.7m);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SelectAsync_SemCandidatosRetornaBaixaConfiancaEWarning()
    {
        var sut = new HeuristicAssignmentContextSelectionService();

        var result = await sut.SelectAsync(
            new AssignmentContextSelectionRequest(
                CourseId: "29972",
                AssignmentId: "101112",
                AssignmentName: "Envio SAP 01 - Etapa 1",
                AssignmentDescription: null,
                Candidates: []),
            CancellationToken.None);

        Assert.Null(result.SelectedCandidateId);
        Assert.Equal("none", result.Classification);
        Assert.Equal(0m, result.Confidence);
        Assert.Contains(result.Warnings, warning => warning.Contains("candidato", StringComparison.OrdinalIgnoreCase));
    }
}
