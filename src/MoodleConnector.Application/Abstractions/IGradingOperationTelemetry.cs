namespace MoodleConnector.Application.Abstractions;

/// <summary>
/// Métricas agregadas das fases da correção. Implementações não devem receber
/// texto acadêmico, nomes, tokens ou URLs autenticadas.
/// </summary>
public interface IGradingOperationTelemetry
{
    void RecordPhase(
        string operation,
        string phase,
        string result,
        double durationMs,
        int queryCount = 0,
        int itemCount = 0,
        long bytes = 0);
}
