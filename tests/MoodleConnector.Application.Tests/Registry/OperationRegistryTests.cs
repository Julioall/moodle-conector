using MoodleConnector.Application.Registry;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Tests.Registry;

public sealed class OperationRegistryTests
{
    [Fact]
    public void RegistersAllKnownReadFunctionsAndControlledWrites()
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
    public void DoesNotInventUnknownOperations()
    {
        var registry = new OperationRegistry();

        Assert.Null(registry.GetOperation("local_plugin_unknown_function"));
    }

    [Fact]
    public void Marks_only_explicitly_shadow_validated_operations_as_live_validated()
    {
        var registry = new OperationRegistry();

        Assert.Equal(ValidationStatus.LiveValidated, registry.GetOperation("core_webservice_get_site_info")!.Status);
        Assert.Equal(ValidationStatus.LiveValidated, registry.GetOperation("mod_assign_get_submissions")!.Status);
        Assert.Equal(ValidationStatus.Registered, registry.GetOperation("core_course_search_courses")!.Status);
    }
}
