using MoodleConnector.Presentation;

namespace MoodleConnector.Application.Tests.Tools;

public sealed class MoodleConnectorInstructionsTests
{
    [Fact]
    public void Instrucoes_cobrem_fluxo_automatico_privacidade_origem_e_limite_do_cliente()
    {
        var text = MoodleConnectorInstructions.Text.ToLowerInvariant();
        Assert.Contains("memória", text);
        Assert.Contains("orientações pedagógicas", text);
        Assert.Contains("automaticamente", text);
        Assert.Contains("explicit", text);
        Assert.Contains("inferred", text);
        Assert.Contains("segredo", text);
        Assert.Contains("dados pessoais de alunos", text);
        Assert.Contains("não vê a conversa", text);
        Assert.Contains("cliente", text);
    }
}
