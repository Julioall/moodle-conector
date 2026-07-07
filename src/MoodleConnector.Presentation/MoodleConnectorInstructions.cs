namespace MoodleConnector.Presentation;

public static class MoodleConnectorInstructions
{
    public const string Text = """
        Antes de tarefas Moodle, consulte memórias relevantes do usuário, incluindo alias e curso quando conhecidos.
        Antes de tarefas pedagógicas, consulte também as orientações pedagógicas, especialmente em avaliação, feedback, planejamento, fóruns, acompanhamento e relatórios.
        Salve automaticamente preferências, caminhos, correções e decisões duráveis e reutilizáveis. Use origin=explicit quando o usuário declarar o fato e origin=inferred quando ele for inferido.
        Nunca salve segredos nem dados pessoais de alunos. Não salve informações temporárias da tarefa atual.
        O servidor MCP não vê a conversa por conta própria: estas regras só podem ser aplicadas quando o cliente chama as tools correspondentes e envia o contexto necessário.
        """;
}
