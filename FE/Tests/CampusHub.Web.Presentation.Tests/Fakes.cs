using Dev4u.CampusHub.Web.Presentation.Errors;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Services;
using FluentResults;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

// Hand-written fakes: the tests script the answers of the backend call after call, which reads better than mocks
// for sequences such as "seat held, then confirmed".

internal static class TestData
{
    public static readonly DateTimeOffset CourseStart = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public static CourseItem Course(string title = "Clean architecture", int seatsLeft = 10, CourseState state = CourseState.Published) =>
        new(Guid.NewGuid(), "ARCH-101", title, "teacher-1", 30, seatsLeft, 0, CourseStart, CourseStart.AddDays(30), state,
            state == CourseState.Draft ? null : DateTimeOffset.UnixEpoch);

    public static EnrollmentItem Enrollment(Guid id, EnrollmentState state) =>
        new(id, Guid.NewGuid(), "Clean architecture", CourseStart, 0, state, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    public static Result<T> Transient<T>() => Result.Fail<T>(UiError.Unavailable());

    public static Result<T> Refused<T>(string code) => Result.Fail<T>(new UiError(code, code));
}

internal sealed class FakeEnrollmentService : IEnrollmentService
{
    private readonly Queue<Result<EnrollmentItem>> _requestResults = new();
    private readonly Queue<Result<IReadOnlyList<EnrollmentItem>>> _listResults = new();
    private Result<IReadOnlyList<EnrollmentItem>> _lastList = Result.Ok<IReadOnlyList<EnrollmentItem>>([]);

    public List<Guid> RequestIds { get; } = [];

    public int ListCalls { get; private set; }

    public void AnswerRequest(Result<EnrollmentItem> result) => _requestResults.Enqueue(result);

    /// <summary>Queues the next answers of ListMineAsync; the last one is repeated once the queue is empty.</summary>
    public void AnswerList(params EnrollmentItem[][] lists)
    {
        foreach (var list in lists)
        {
            _listResults.Enqueue(Result.Ok<IReadOnlyList<EnrollmentItem>>(list));
        }
    }

    public void AnswerListResult(Result<IReadOnlyList<EnrollmentItem>> result) => _listResults.Enqueue(result);

    public Task<Result<EnrollmentItem>> RequestAsync(Guid requestId, Guid courseId, CancellationToken cancellationToken)
    {
        RequestIds.Add(requestId);
        return Task.FromResult(_requestResults.Count > 0
            ? _requestResults.Dequeue()
            : Result.Ok(TestData.Enrollment(requestId, EnrollmentState.Requested)));
    }

    public Task<Result<IReadOnlyList<EnrollmentItem>>> ListMineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ListCalls++;
        if (_listResults.Count > 0)
        {
            _lastList = _listResults.Dequeue();
        }

        return Task.FromResult(_lastList);
    }
}

internal sealed class FakeCurrentUserService(CurrentUser user) : ICurrentUserService
{
    public Task<Result<CurrentUser>> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Ok(user));
}

internal sealed class FakeCourseCatalog : ICourseCatalog
{
    public Result<IReadOnlyList<CourseItem>> Published { get; set; } = Result.Ok<IReadOnlyList<CourseItem>>([]);

    public Result<IReadOnlyList<CourseItem>> Mine { get; set; } = Result.Ok<IReadOnlyList<CourseItem>>([]);

    public (string Code, string Title, int Capacity, int PriceInCents, DateTimeOffset StartsAt, DateTimeOffset EndsAt)? Created { get; private set; }

    public Result<CourseItem>? CreateResult { get; set; }

    public Task<Result<IReadOnlyList<CourseItem>>> ListPublishedAsync(CancellationToken cancellationToken) => Task.FromResult(Published);

    public Task<Result<IReadOnlyList<CourseItem>>> ListMineAsync(CancellationToken cancellationToken) => Task.FromResult(Mine);

    public Task<Result<CourseItem>> CreateAsync(string code, string title, int capacity, int priceInCents, DateTimeOffset startsAt,
                                                DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        Created = (code, title, capacity, priceInCents, startsAt, endsAt);
        return Task.FromResult(CreateResult
            ?? Result.Ok(new CourseItem(Guid.NewGuid(), code, title, "teacher-1", capacity, capacity, priceInCents, startsAt, endsAt,
                                        CourseState.Draft, null)));
    }

    public Task<Result<CourseItem>> PublishAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = Mine.Value.Single(c => c.Id == courseId);
        return Task.FromResult(Result.Ok(course with { State = CourseState.Published, PublishedAt = DateTimeOffset.UnixEpoch }));
    }
}
