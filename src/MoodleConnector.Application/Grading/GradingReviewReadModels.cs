using MediatR;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Grading;

public sealed record GetGradingReviewPageQuery(
    Guid BatchJobId,
    int Page = 1,
    int PageSize = 50) : IRequest<GradingReviewPageReadModel>;

public sealed class GetGradingReviewPageQueryHandler(
    IGradingReviewRepository repository,
    IGradingReviewReadStore readStore,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetGradingReviewPageQuery, GradingReviewPageReadModel>
{
    public async Task<GradingReviewPageReadModel> Handle(
        GetGradingReviewPageQuery request,
        CancellationToken cancellationToken)
    {
        if (request.BatchJobId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(request.BatchJobId));
        }

        var batch = await repository.GetBatchAsync(request.BatchJobId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de correcao nao encontrado.");
        GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);

        return await readStore.GetPageAsync(
            batch.Id,
            Math.Max(1, request.Page),
            Math.Clamp(request.PageSize, 1, 100),
            cancellationToken);
    }
}
