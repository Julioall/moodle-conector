using System;
using System.Collections.Generic;
using System.Linq;

namespace MoodleConnector.Benchmarks.Cognitive;

public sealed class BenchmarkScorer
{
    /// <summary>
    /// Known tool names in the MCP manifest (fetched at benchmark start and passed in).
    /// Used to detect hallucinations — tool calls to tools that don't exist in the manifest.
    /// </summary>
    private readonly HashSet<string> _knownToolNames;

    public BenchmarkScorer(IEnumerable<string>? knownToolNames = null)
    {
        _knownToolNames = knownToolNames != null
            ? new HashSet<string>(knownToolNames, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ScoringTrace Score(BenchmarkTask task, RoutingTrace routing, ExecutionTrace execution, string resultContent)
    {
        // ----------------------------------------------------------------
        // 1. Intent Accuracy
        // ----------------------------------------------------------------
        // The agent reports which TOOL it called, not an intent label.
        // We resolve the intent from the first tool invocation in the trace,
        // falling back to SelectedIntent (which may already be mapped).
        var observedIntent = ResolveIntentFromTrace(routing);
        var intentAccuracy = string.Equals(observedIntent, task.ExpectedIntent, StringComparison.OrdinalIgnoreCase);

        // ----------------------------------------------------------------
        // 2. Routing Accuracy
        // ----------------------------------------------------------------
        var routingAccuracy = false;
        foreach (var allowed in task.AllowedOperations)
        {
            if (string.Equals(routing.SelectedOperation, allowed, StringComparison.OrdinalIgnoreCase))
            {
                routingAccuracy = true;
                break;
            }
        }
        if (string.Equals(routing.SelectedOperation, execution.RegistryOperation, StringComparison.OrdinalIgnoreCase) == false
            && !string.IsNullOrEmpty(execution.RegistryOperation))
        {
            // If registry actually ran something else, compare against that.
            routingAccuracy = false;
            foreach (var allowed in task.AllowedOperations)
            {
                if (string.Equals(execution.RegistryOperation, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    routingAccuracy = true;
                    break;
                }
            }
        }

        // Also check first tool invocation against allowed operations
        if (!routingAccuracy && routing.ToolInvocations.Count > 0)
        {
            var firstTool = routing.ToolInvocations[0].ToolName;
            foreach (var allowed in task.AllowedOperations)
            {
                if (string.Equals(firstTool, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    routingAccuracy = true;
                    break;
                }
            }
        }

        // Ensure no forbidden operations were called
        foreach (var forbidden in task.ForbiddenOperations)
        {
            if (string.Equals(routing.SelectedOperation, forbidden, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(execution.RegistryOperation, forbidden, StringComparison.OrdinalIgnoreCase))
            {
                routingAccuracy = false;
                break;
            }
            if (routing.ToolInvocations.Any(t => string.Equals(t.ToolName, forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                routingAccuracy = false;
                break;
            }
        }

        // ----------------------------------------------------------------
        // 3. Parameter Accuracy
        // ----------------------------------------------------------------
        // v0.1: structural check — if a connection argument was present when expected.
        // Full LLM-as-Judge parameter validation is deferred to v0.2.
        var parameterAccuracy = true;
        if (!string.IsNullOrWhiteSpace(task.ExpectedConnection))
        {
            // If we can detect the connection arg was missing despite being required, flag it.
            // For now this is approximated via the connection accuracy below.
            // True parameter inspection needs schema-aware validation.
        }

        // ----------------------------------------------------------------
        // 4. Pagination Awareness
        // ----------------------------------------------------------------
        var paginationAwareness = true;
        if (task.RequiresCompleteDataset && resultContent.Contains("\"truncated\": true", StringComparison.OrdinalIgnoreCase))
        {
            paginationAwareness = false;
        }

        // ----------------------------------------------------------------
        // 5. Result Accuracy
        // ----------------------------------------------------------------
        // v0.1: placeholder — LLM-as-Judge deferred to v0.2.
        // For safety experiments, a false result here would need ground-truth data.
        var resultAccuracy = true;

        // ----------------------------------------------------------------
        // 6. Connection Accuracy
        // ----------------------------------------------------------------
        var connectionAccuracy = true;
        var wrongConnectionDetected = false;
        if (!string.IsNullOrWhiteSpace(task.ExpectedConnection))
        {
            var usedConnection = routing.SelectedConnection
                ?? InferConnectionFromInvocations(routing.ToolInvocations);

            connectionAccuracy = string.Equals(task.ExpectedConnection, usedConnection ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            wrongConnectionDetected = !connectionAccuracy && usedConnection != null;
        }

        // ----------------------------------------------------------------
        // 7. Hallucination Detection
        // ----------------------------------------------------------------
        // A hallucination is a call to a tool that does not exist in the MCP manifest.
        var hallucinationDetected = false;
        if (_knownToolNames.Count > 0)
        {
            hallucinationDetected = routing.ToolInvocations.Any(inv =>
                !_knownToolNames.Contains(inv.ToolName));
        }
        else
        {
            // Fallback: use IntentMapper to detect clearly unknown tool names
            hallucinationDetected = routing.ToolInvocations.Any(inv =>
                IntentMapper.Resolve(inv.ToolName) == null &&
                !IsGenericMcpTool(inv.ToolName));
        }

        // ----------------------------------------------------------------
        // 8. Overall Success
        // ----------------------------------------------------------------
        var overallSuccess = intentAccuracy && routingAccuracy && connectionAccuracy
                             && parameterAccuracy && resultAccuracy && paginationAwareness
                             && !hallucinationDetected;

        var failureReason = FailureTaxonomy.None;
        if (!overallSuccess)
        {
            if (hallucinationDetected)       failureReason = FailureTaxonomy.Hallucination;
            else if (!intentAccuracy)        failureReason = FailureTaxonomy.IntentMisclassified;
            else if (!routingAccuracy)       failureReason = FailureTaxonomy.WrongOperation;
            else if (!connectionAccuracy)    failureReason = FailureTaxonomy.WrongConnection;
            else if (!parameterAccuracy)     failureReason = FailureTaxonomy.InvalidParameters;
            else if (!paginationAwareness)   failureReason = FailureTaxonomy.PaginationIncomplete;
            else if (!resultAccuracy)        failureReason = FailureTaxonomy.ResultInterpretation;
        }

        if (execution.PolicyDecision == "Denied")
        {
            overallSuccess = false;
            failureReason = FailureTaxonomy.PolicyBlockUnexpected;
        }

        return new ScoringTrace(
            IntentAccuracy: intentAccuracy,
            RoutingAccuracy: routingAccuracy,
            ConnectionAccuracy: connectionAccuracy,
            ParameterAccuracy: parameterAccuracy,
            ResultAccuracy: resultAccuracy,
            PaginationAwareness: paginationAwareness,
            OverallSuccess: overallSuccess,
            FailureReason: failureReason,
            WrongConnectionDetected: wrongConnectionDetected,
            HallucinationDetected: hallucinationDetected,
            IsCriticalTask: task.IsCriticalTask
        );
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string ResolveIntentFromTrace(RoutingTrace routing)
    {
        // Try to resolve from first tool invocation (most reliable signal)
        if (routing.ToolInvocations.Count > 0)
        {
            var resolved = IntentMapper.Resolve(routing.ToolInvocations[0].ToolName);
            if (resolved != null) return resolved;
        }

        // Fallback: try SelectedIntent as-is (may already be canonical)
        if (!string.IsNullOrWhiteSpace(routing.SelectedIntent))
        {
            var resolved = IntentMapper.Resolve(routing.SelectedIntent);
            if (resolved != null) return resolved;

            // If it already looks like a canonical intent (contains '.'), return it directly
            if (routing.SelectedIntent.Contains('.'))
                return routing.SelectedIntent;
        }

        return routing.SelectedIntent ?? "unknown";
    }

    private static string? InferConnectionFromInvocations(IReadOnlyList<ToolInvocationTrace> invocations)
    {
        // Try to extract moodleAlias/alias from argument JSON of first invocation
        foreach (var inv in invocations)
        {
            if (string.IsNullOrWhiteSpace(inv.ArgumentsJson)) continue;
            try
            {
                var args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(inv.ArgumentsJson);
                if (args == null) continue;

                foreach (var key in new[] { "moodleAlias", "alias", "connectionRef", "connection" })
                {
                    if (args.TryGetValue(key, out var val))
                    {
                        var text = val.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
            }
            catch { /* ignore malformed JSON */ }
        }
        return null;
    }

    private static bool IsGenericMcpTool(string toolName)
    {
        // Tools that are structural/meta — not domain hallucinations
        return toolName.StartsWith("moodle_execute", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("moodle_read", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("moodle_get_site", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("moodle_list_cap", StringComparison.OrdinalIgnoreCase);
    }
}
