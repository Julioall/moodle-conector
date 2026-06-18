using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Completion.Queries;

namespace MoodleConnector.Application.Tests.Completion.Queries;

public sealed class GetStudentCompletionQueryHandlerTests
{
    [Fact]
    public async Task Handle_DeveRetornarProgressoDoGateway()
    {
        var gateway = new FakeMoodleCompletionGateway();
        var sut = new GetStudentCompletionQueryHandler(gateway);

        var result = await sut.Handle(new GetStudentCompletionQuery("10", "123"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Completed);
        Assert.Single(result.Activities);
        Assert.Equal("assign", result.Activities.First().Modname);
    }

    private sealed class FakeMoodleCompletionGateway : IMoodleCompletionGateway
    {
        public Task<CourseCompletionStatus> GetStudentCompletionAsync(string courseId, string studentId, CancellationToken cancellationToken)
        {
            var activities = new List<ActivityCompletionStatus>
            {
                new ActivityCompletionStatus(
                    Cmid: "1",
                    Modname: "assign",
                    Instance: "2",
                    State: 1,
                    Timecompleted: 1600000000,
                    Tracking: 1,
                    Overrideby: null,
                    Valueused: false)
            };
            
            return Task.FromResult(new CourseCompletionStatus(
                Completed: true,
                Timecompleted: 1600000100,
                Activities: activities));
        }
    }
}
