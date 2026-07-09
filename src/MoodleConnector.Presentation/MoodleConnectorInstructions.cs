namespace MoodleConnector.Presentation;

public static class MoodleConnectorInstructions
{
    public const string Text = """
        Antes de tarefas Moodle, consulte memorias relevantes do usuario, incluindo alias e curso quando conhecidos.
        Quando uma memoria da categoria modelo apontar para um documento, leia o documento de memoria antes de gerar o conteudo final.
        Antes de tarefas pedagogicas, consulte tambem as orientacoes pedagogicas, especialmente em avaliacao, feedback, planejamento, foruns, acompanhamento e relatorios.
        Salve automaticamente preferencias, caminhos, correcoes, decisoes e modelos duraveis e reutilizaveis. Para modelos extensos, salve o conteudo completo como documento de memoria e use a memoria curta apenas como link semantico.
        Use origin=explicit quando o usuario declarar o fato e origin=inferred quando ele for inferido.
        Nunca salve segredos nem dados pessoais de alunos. Nao salve informacoes temporarias da tarefa atual.
        O servidor MCP nao ve a conversa por conta propria: estas regras so podem ser aplicadas quando o cliente chama as tools correspondentes e envia o contexto necessario.
        """;
}
