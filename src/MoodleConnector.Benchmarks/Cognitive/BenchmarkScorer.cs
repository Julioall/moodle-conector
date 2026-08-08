using System;

namespace MoodleConnector.Benchmarks.Cognitive;

public sealed class BenchmarkScorer
{
    public ScoringTrace Score(BenchmarkTask task, RoutingTrace routing, ExecutionTrace execution, string resultContent)
    {
        // 1. Intent Accuracy
        var intentAccuracy = string.Equals(routing.SelectedIntent, task.ExpectedIntent, StringComparison.OrdinalIgnoreCase);

        // 2. Routing Accuracy
        var routingAccuracy = false;
        foreach (var allowed in task.AllowedOperations)
        {
            if (string.Equals(routing.SelectedOperation, allowed, StringComparison.OrdinalIgnoreCase))
            {
                routingAccuracy = true;
                break;
            }
        }
        if (string.Equals(routing.SelectedOperation, execution.RegistryOperation, StringComparison.OrdinalIgnoreCase) == false && !string.IsNullOrEmpty(execution.RegistryOperation))
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
        
        // Ensure no forbidden operations were called
        foreach (var forbidden in task.ForbiddenOperations)
        {
            if (string.Equals(routing.SelectedOperation, forbidden, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(execution.RegistryOperation, forbidden, StringComparison.OrdinalIgnoreCase))
            {
                routingAccuracy = false;
                break;
            }
        }

        // 3. Parameter Accuracy
        // Simplification for v0.1: Assuming true if not explicitly wrong. Real LLM trace would inspect schema matching.
        var parameterAccuracy = true; 

        // 4. Pagination Awareness
        var paginationAwareness = true;
        if (task.RequiresCompleteDataset && resultContent.Contains("\"truncated\": true", StringComparison.OrdinalIgnoreCase))
        {
            // If the prompt requires complete dataset, and the raw result has truncated = true,
            // the LLM should ideally have continued fetching. If this is the final trace and it's truncated,
            // we mark pagination awareness as false if it didn't fetch all.
            // (For v0.1 we rely on the final text or if the agent loop stopped early)
            paginationAwareness = false;
        }

        // 5. Result Accuracy
        // Did it find the correct final answer? Need LLM as Judge or exact string matching. For now, structural mock.
        var resultAccuracy = true;

        // 6. Connection Accuracy
        var connectionAccuracy = true;
        if (!string.IsNullOrWhiteSpace(task.ExpectedConnection))
        {
            connectionAccuracy = string.Equals(task.ExpectedConnection, routing.SelectedConnection ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        // Overall
        var overallSuccess = intentAccuracy && routingAccuracy && connectionAccuracy && parameterAccuracy && resultAccuracy && paginationAwareness;

        var failureReason = FailureTaxonomy.None;
        if (!overallSuccess)
        {
            if (!intentAccuracy) failureReason = FailureTaxonomy.IntentMisclassified;
            else if (!routingAccuracy) failureReason = FailureTaxonomy.WrongOperation;
            else if (!connectionAccuracy) failureReason = FailureTaxonomy.WrongConnection;
            else if (!parameterAccuracy) failureReason = FailureTaxonomy.InvalidParameters;
            else if (!paginationAwareness) failureReason = FailureTaxonomy.PaginationIncomplete;
            else if (!resultAccuracy) failureReason = FailureTaxonomy.ResultInterpretation;
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
            FailureReason: failureReason
        );
    }
}
