using MoodleConnector.Application.Registry;
using MoodleConnector.Application.MoodleApi;
using System.Text.Json;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Tests.Registry;

public sealed class OperationRegistryTests
{
    [Fact]
    public void ClassifiesReadAndWriteFunctionsWithoutAStaticInventory()
    {
        var registry = new OperationRegistry();

        var read = registry.GetOperation("core_course_search_courses");
        var write = registry.GetOperation("mod_assign_save_grade");

        Assert.NotNull(read);
        Assert.Equal(OperationType.Read, read!.Type);
        Assert.Equal("course", read.Category);
        Assert.NotNull(write);
        Assert.Equal(OperationType.ControlledWrite, write!.Type);
        Assert.Equal(OperationPolicy.Aggregated, write.Policy);
    }

    [Fact]
    public void RoutesAnUnrecognizedFunctionToConfirmedWrite()
    {
        var registry = new OperationRegistry();

        var operation = registry.GetOperation("local_plugin_unknown_function");

        Assert.NotNull(operation);
        Assert.Equal(OperationType.ControlledWrite, operation!.Type);
    }

    [Fact]
    public void TreatsPaginatedForumQueriesAsReads()
    {
        var registry = new OperationRegistry();

        var operation = registry.GetOperation("mod_forum_get_forum_discussions_paginated");

        Assert.NotNull(operation);
        Assert.Equal(OperationType.Read, operation!.Type);
        Assert.Equal("forum", operation.Category);
    }

    [Fact]
    public void VerifiedContractTakesPrecedenceOverFunctionNameHeuristic()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "mod_example_view_item",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };

        var registry = new OperationRegistry(new MoodleFunctionContractRegistry([contract]));

        var operation = registry.GetOperation(contract.FunctionName);

        Assert.NotNull(operation);
        Assert.Equal(OperationType.Read, operation!.Type);
    }
}
