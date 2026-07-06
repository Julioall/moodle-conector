using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Participants;

public sealed class ParticipantClassificationTests
{
    [Fact]
    public void Classifica_papel_de_aluno()
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("5", "student", "Estudante")));

        Assert.Equal(ParticipantClassificationKind.Student, result);
    }

    [Fact]
    public void Inclui_participante_sem_papel_por_fallback()
    {
        var result = ParticipantClassification.Classify(CreateParticipant());

        Assert.Equal(ParticipantClassificationKind.UncertainFallback, result);
    }

    [Fact]
    public void Exclui_papel_preenchido_que_nao_e_student()
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("9", "customrole", "Papel local")));

        Assert.Equal(ParticipantClassificationKind.KnownStaff, result);
    }

    [Theory]
    [InlineData("monitor_go", "Monitor - GO")]
    [InlineData("editingteacher-go", "Professor - GO")]
    [InlineData("teacher", "Professor")]
    [InlineData("professor", "Professor")]
    [InlineData("monitor", "Monitor")]
    [InlineData("tutor", "Tutor")]
    [InlineData("manager", "Gestor")]
    [InlineData("coordinator", "Coordenador")]
    public void Exclui_roles_nao_estudantis_confirmadas(string shortName, string name)
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("9", shortName, name)));

        Assert.Equal(ParticipantClassificationKind.KnownStaff, result);
    }

    [Fact]
    public void Exclui_perfil_exclusivamente_de_equipe()
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("3", "editingteacher", "Professor")));

        Assert.Equal(ParticipantClassificationKind.KnownStaff, result);
    }

    [Fact]
    public void Inclui_papel_misto_quando_um_deles_e_aluno()
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("5", "student", "Estudante"),
            new CourseParticipantRole("3", "teacher", "Professor")));

        Assert.Equal(ParticipantClassificationKind.Student, result);
    }

    [Fact]
    public void Reconhece_alias_com_maiusculas_e_acentos()
    {
        var result = ParticipantClassification.Classify(CreateParticipant(
            new CourseParticipantRole("5", null, "ALÚNO")));

        Assert.Equal(ParticipantClassificationKind.Student, result);
    }

    private static CourseParticipantSummary CreateParticipant(params CourseParticipantRole[] roles)
    {
        return new CourseParticipantSummary(
            "123", "Pessoa Teste", null, false, null, null, null, roles, []);
    }
}
