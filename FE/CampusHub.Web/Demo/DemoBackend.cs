using Dev4u.CampusHub.Web.Presentation.Errors;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Services;
using FluentResults;

namespace Dev4u.CampusHub.Web.Demo;

/// <summary>
/// In-memory stand-in for the services, used by the Demo mode ("dotnet run" on this project) to work on the UI
/// without the backend. It reproduces what the UI must cope with: network latency, a full course, an idempotent
/// enrollment request and the saga moving from Requested to SeatHeld to Confirmed in the background.
/// </summary>
internal sealed class DemoCampus
{
    public const string UserId = "demo-user";
    public const string UserName = "Demo user";

    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(250);
    private readonly TimeProvider _time;
    private readonly List<DemoCourse> _courses;
    private readonly Dictionary<Guid, EnrollmentItem> _enrollments = [];
    private readonly List<NotificationItem> _notifications = [];

    public DemoCampus(TimeProvider time)
    {
        _time = time;
        var now = time.GetUtcNow();
        _courses =
        [
            new("DOTNET-101", "Modern .NET from scratch", "prof-ada", 30, 12, Day(now, 7), Day(now, 37), CourseState.Published, now.AddDays(-3)),
            new("DAPR-201", "Event-driven microservices with Dapr", "prof-ada", 20, 19, Day(now, 14), Day(now, 28), CourseState.Published, now.AddDays(-2)),
            new("BLAZOR-110", "Blazor and MVVM in practice", "prof-grace", 25, 4, Day(now, 1), Day(now, 21), CourseState.Published, now.AddDays(-1)),
            new("K8S-301", "Kubernetes for .NET developers", "prof-grace", 1, 1, Day(now, 30), Day(now, 60), CourseState.Published, now.AddHours(-5)),
            new("OTEL-120", "Observability with OpenTelemetry", UserId, 40, 0, Day(now, 45), Day(now, 75), CourseState.Draft, null),
        ];
    }

    public Task DelayAsync(CancellationToken cancellationToken) => Task.Delay(Latency, _time, cancellationToken);

    private static DateTimeOffset Day(DateTimeOffset now, int days) => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(days).AddHours(9);

    public IReadOnlyList<CourseItem> Published() =>
        [.. _courses.Where(c => c.State == CourseState.Published).OrderByDescending(c => c.PublishedAt).Select(c => c.ToItem())];

    public IReadOnlyList<CourseItem> Mine() => [.. _courses.Where(c => c.TeacherId == UserId).Select(c => c.ToItem())];

    public Result<CourseItem> Create(string code, string title, int capacity, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (_courses.Exists(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail<CourseItem>(new UiError("course.code_taken", "Code already used."));
        }

        var course = new DemoCourse(code.ToUpperInvariant(), title, UserId, capacity, 0, startsAt, endsAt, CourseState.Draft, null);
        _courses.Insert(0, course);
        return Result.Ok(course.ToItem());
    }

    public Result<CourseItem> Publish(Guid courseId)
    {
        var course = _courses.Find(c => c.Id == courseId);
        if (course is null)
        {
            return Result.Fail<CourseItem>(new UiError("course.not_found", "Course not found."));
        }

        course.State = CourseState.Published;
        course.PublishedAt = _time.GetUtcNow();
        return Result.Ok(course.ToItem());
    }

    public Result<EnrollmentItem> Request(Guid requestId, Guid courseId)
    {
        if (_enrollments.TryGetValue(requestId, out var existing))
        {
            return Result.Ok(existing); // same request id: same enrollment, like the real service
        }

        var course = _courses.Find(c => c.Id == courseId);
        if (course is null)
        {
            return Result.Fail<EnrollmentItem>(new UiError("course.not_found", "Course not found."));
        }

        if (_enrollments.Values.Any(e => e.CourseId == courseId && e.State != EnrollmentState.Rejected))
        {
            return Result.Fail<EnrollmentItem>(new UiError("course.already_enrolled", "Already enrolled."));
        }

        var now = _time.GetUtcNow();
        var enrollment = new EnrollmentItem(requestId, courseId, course.Title, course.StartsAt, EnrollmentState.Requested, null, now, now);
        _enrollments[requestId] = enrollment;
        _ = RunSagaAsync(requestId, course); // demo only: the real saga runs in the Enrollment service
        return Result.Ok(enrollment);
    }

    public IReadOnlyList<EnrollmentItem> MyEnrollments() => [.. _enrollments.Values.OrderByDescending(e => e.RequestedAt)];

    public IReadOnlyList<NotificationItem> Notifications() => [.. _notifications];

    private async Task RunSagaAsync(Guid enrollmentId, DemoCourse course)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5), _time);
        if (course.Taken >= course.Capacity)
        {
            Update(enrollmentId, EnrollmentState.Rejected, "course.full");
            return;
        }

        course.Taken++;
        Update(enrollmentId, EnrollmentState.SeatHeld, null);

        await Task.Delay(TimeSpan.FromSeconds(2), _time);
        Update(enrollmentId, EnrollmentState.Confirmed, null);
        _notifications.Insert(0, new NotificationItem(Guid.NewGuid(), "Enrollment confirmed",
            $"Your enrollment in \"{course.Title}\" is confirmed.", _time.GetUtcNow(), IsRead: false));
    }

    private void Update(Guid enrollmentId, EnrollmentState state, string? rejectionCode) =>
        _enrollments[enrollmentId] = _enrollments[enrollmentId] with
        {
            State = state,
            RejectionCode = rejectionCode,
            UpdatedAt = _time.GetUtcNow(),
        };

    private sealed class DemoCourse(string code, string title, string teacherId, int capacity, int taken, DateTimeOffset startsAt,
                                    DateTimeOffset endsAt, CourseState state, DateTimeOffset? publishedAt)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Code { get; } = code;
        public string Title { get; } = title;
        public string TeacherId { get; } = teacherId;
        public int Capacity { get; } = capacity;
        public int Taken { get; set; } = taken;
        public DateTimeOffset StartsAt { get; } = startsAt;
        public DateTimeOffset EndsAt { get; } = endsAt;
        public CourseState State { get; set; } = state;
        public DateTimeOffset? PublishedAt { get; set; } = publishedAt;

        public CourseItem ToItem() => new(Id, Code, Title, TeacherId, Capacity, Math.Max(0, Capacity - Taken), StartsAt, EndsAt, State, PublishedAt);
    }
}

internal sealed class DemoCourseCatalog(DemoCampus campus) : ICourseCatalog
{
    public async Task<Result<IReadOnlyList<CourseItem>>> ListPublishedAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Published());
    }

    public async Task<Result<IReadOnlyList<CourseItem>>> ListMineAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Mine());
    }

    public async Task<Result<CourseItem>> CreateAsync(string code, string title, int capacity, DateTimeOffset startsAt, DateTimeOffset endsAt,
                                                      CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Create(code, title, capacity, startsAt, endsAt);
    }

    public async Task<Result<CourseItem>> PublishAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Publish(courseId);
    }
}

internal sealed class DemoEnrollmentService(DemoCampus campus) : IEnrollmentService
{
    public async Task<Result<EnrollmentItem>> RequestAsync(Guid requestId, Guid courseId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Request(requestId, courseId);
    }

    public async Task<Result<IReadOnlyList<EnrollmentItem>>> ListMineAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.MyEnrollments());
    }
}

internal sealed class DemoNotificationService(DemoCampus campus) : INotificationService
{
    public async Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Notifications());
    }
}

internal sealed class DemoCurrentUserService : ICurrentUserService
{
    public Task<Result<CurrentUser>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok(new CurrentUser(DemoCampus.UserId, DemoCampus.UserName, IsTeacher: true, IsAdmin: false)));
}

internal static class DemoBackend
{
    public static IServiceCollection AddDemoBackend(this IServiceCollection services)
    {
        services.AddSingleton<DemoCampus>();
        services.AddScoped<ICourseCatalog, DemoCourseCatalog>();
        services.AddScoped<IEnrollmentService, DemoEnrollmentService>();
        services.AddScoped<INotificationService, DemoNotificationService>();
        services.AddScoped<ICurrentUserService, DemoCurrentUserService>();
        return services;
    }
}
