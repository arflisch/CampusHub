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
            new("DOTNET-101", "Modern .NET from scratch", "prof-ada", 30, 12, 0, Day(now, 7), Day(now, 37), CourseState.Published, now.AddDays(-3)),
            new("DAPR-201", "Event-driven microservices with Dapr", "prof-ada", 20, 19, 14900, Day(now, 14), Day(now, 28), CourseState.Published, now.AddDays(-2)),
            new("BLAZOR-110", "Blazor and MVVM in practice", "prof-grace", 25, 4, 4900, Day(now, 1), Day(now, 21), CourseState.Published, now.AddDays(-1)),
            new("ARCH-900", "Enterprise architecture bootcamp", "prof-grace", 10, 2, 89000, Day(now, 20), Day(now, 50), CourseState.Published, now.AddHours(-8)),
            new("K8S-301", "Kubernetes for .NET developers", "prof-grace", 1, 1, 9900, Day(now, 30), Day(now, 60), CourseState.Published, now.AddHours(-5)),
            new("OTEL-120", "Observability with OpenTelemetry", UserId, 40, 0, 0, Day(now, 45), Day(now, 75), CourseState.Draft, null),
        ];
    }

    public Task DelayAsync(CancellationToken cancellationToken) => Task.Delay(Latency, _time, cancellationToken);

    private static DateTimeOffset Day(DateTimeOffset now, int days) => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(days).AddHours(9);

    public IReadOnlyList<CourseItem> Published() =>
        [.. _courses.Where(c => c.State == CourseState.Published).OrderByDescending(c => c.PublishedAt).Select(c => c.ToItem())];

    public IReadOnlyList<CourseItem> Mine() => [.. _courses.Where(c => c.TeacherId == UserId).Select(c => c.ToItem())];

    /// <summary>Like the simulated provider of Billing: a card is declined above this amount.</summary>
    private const int CardLimitInCents = 50_000;

    public Result<CourseItem> Create(string code, string title, int capacity, int priceInCents, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (_courses.Exists(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail<CourseItem>(new UiError("course.code_taken", "Code already used."));
        }

        var course = new DemoCourse(code.ToUpperInvariant(), title, UserId, capacity, 0, priceInCents, startsAt, endsAt, CourseState.Draft, null);
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

        if (_enrollments.Values.Any(e => e.CourseId == courseId && e.State is not (EnrollmentState.Rejected or EnrollmentState.Cancelled)))
        {
            return Result.Fail<EnrollmentItem>(new UiError("course.already_enrolled", "Already enrolled."));
        }

        var now = _time.GetUtcNow();
        var enrollment = new EnrollmentItem(requestId, courseId, course.Title, course.StartsAt, course.PriceInCents, EnrollmentState.Requested,
                                            null, now, now);
        _enrollments[requestId] = enrollment;
        _ = RunSagaAsync(requestId, course); // demo only: the real saga runs in the Enrollment service
        return Result.Ok(enrollment);
    }

    public IReadOnlyList<EnrollmentItem> MyEnrollments() => [.. _enrollments.Values.OrderByDescending(e => e.RequestedAt)];

    public IReadOnlyList<NotificationItem> Notifications() => [.. _notifications];

    /// <summary>Raised like the server's live pushes (SignalR) would be.</summary>
    public event EventHandler<EnrollmentChange>? EnrollmentChanged;

    public event EventHandler<NotificationItem>? NotificationReceived;

    private static readonly string[] SampleStudents = ["Ada Lovelace", "Grace Hopper", "Alan Turing", "Margaret Hamilton", "Linus Torvalds"];

    public Result<EnrollmentItem> Cancel(Guid enrollmentId)
    {
        if (!_enrollments.TryGetValue(enrollmentId, out var enrollment))
        {
            return Result.Fail<EnrollmentItem>(new UiError("enrollment.not_found", "Not found."));
        }

        if (enrollment.State != EnrollmentState.Confirmed)
        {
            return Result.Fail<EnrollmentItem>(new UiError("enrollment.not_cancellable", "Not confirmed."));
        }

        _courses.Find(c => c.Id == enrollment.CourseId)!.Taken--;
        Update(enrollmentId, EnrollmentState.Cancelled, null, "enrollment.cancelled_by_student");
        Notify("Enrollment cancelled", $"Your enrollment in \"{enrollment.CourseTitle}\" is cancelled.{Refund(enrollment.CoursePriceInCents)}");
        return Result.Ok(_enrollments[enrollmentId]);
    }

    public Result<CourseItem> CancelCourse(Guid courseId)
    {
        var course = _courses.Find(c => c.Id == courseId);
        if (course is null || course.TeacherId != UserId)
        {
            return Result.Fail<CourseItem>(new UiError("course.not_owner", "Not your course."));
        }

        course.State = CourseState.Cancelled;
        // What Enrollment does when CourseCancelled arrives: refund and cancel every confirmed enrollment.
        foreach (var enrollment in _enrollments.Values.Where(e => e.CourseId == courseId && e.State == EnrollmentState.Confirmed).ToList())
        {
            Update(enrollment.Id, EnrollmentState.Cancelled, null, "course.cancelled");
            Notify("Course cancelled", $"\"{course.Title}\" was cancelled by its teacher.{Refund(enrollment.CoursePriceInCents)}");
        }

        return Result.Ok(course.ToItem());
    }

    public IReadOnlyList<ParticipantItem> Participants(Guid courseId)
    {
        var course = _courses.Find(c => c.Id == courseId);
        if (course is null || course.TeacherId != UserId)
        {
            return [];
        }

        var now = _time.GetUtcNow();
        return [.. SampleStudents.Take(course.Taken).Select((name, i) =>
            new ParticipantItem(Guid.NewGuid(), $"student-{i + 1}", name, EnrollmentState.Confirmed, course.PriceInCents, now.AddHours(-i - 1)))];
    }

    public IReadOnlyList<PaymentItem> Payments() =>
    [
        .. _enrollments.Values.Where(e => e.CoursePriceInCents > 0 && e.State is not EnrollmentState.Requested and not EnrollmentState.SeatHeld)
                       .Select(e => new PaymentItem(e.Id, e.CourseId, e.CoursePriceInCents,
                                                    e.State switch
                                                    {
                                                        EnrollmentState.Rejected => PaymentState.Declined,
                                                        EnrollmentState.Cancelled => PaymentState.Refunded,
                                                        _ => PaymentState.Captured,
                                                    },
                                                    e.State == EnrollmentState.Rejected ? "card.limit_exceeded" : null,
                                                    e.UpdatedAt, e.State == EnrollmentState.Cancelled ? e.UpdatedAt : null)),
    ];

    public IReadOnlyList<CertificateItem> Certificates()
    {
        var now = _time.GetUtcNow();
        return [new CertificateItem("CH-2026-3F2A9C41D0", Guid.NewGuid(), "Git for teams", now.AddDays(-20), now.AddDays(-19))];
    }

    public void MarkAllRead()
    {
        for (var i = 0; i < _notifications.Count; i++)
        {
            _notifications[i] = _notifications[i] with { IsRead = true };
        }
    }

    private static string Refund(int cents) => cents > 0 ? $" You were refunded {Money.Format(cents)}." : string.Empty;

    private void Notify(string title, string message)
    {
        var notification = new NotificationItem(Guid.NewGuid(), title, message, _time.GetUtcNow(), IsRead: false);
        _notifications.Insert(0, notification);
        NotificationReceived?.Invoke(this, notification);
    }

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

        if (course.PriceInCents > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5), _time);
            if (course.PriceInCents > CardLimitInCents)
            {
                course.Taken--; // compensation: the seat is given back
                Update(enrollmentId, EnrollmentState.Rejected, "payment.declined");
                return;
            }

            Update(enrollmentId, EnrollmentState.Paid, null);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), _time);
        Update(enrollmentId, EnrollmentState.Confirmed, null);
        Notify("Enrollment confirmed", $"Your enrollment in \"{course.Title}\" is confirmed.");
    }

    private void Update(Guid enrollmentId, EnrollmentState state, string? rejectionCode, string? cancellationReason = null)
    {
        var enrollment = _enrollments[enrollmentId] = _enrollments[enrollmentId] with
        {
            State = state,
            RejectionCode = rejectionCode,
            CancellationReason = cancellationReason,
            UpdatedAt = _time.GetUtcNow(),
        };
        EnrollmentChanged?.Invoke(this, new EnrollmentChange(enrollment.Id, enrollment.CourseId, state));
    }

    private sealed class DemoCourse(string code, string title, string teacherId, int capacity, int taken, int priceInCents,
                                    DateTimeOffset startsAt, DateTimeOffset endsAt, CourseState state, DateTimeOffset? publishedAt)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Code { get; } = code;
        public string Title { get; } = title;
        public string TeacherId { get; } = teacherId;
        public int Capacity { get; } = capacity;
        public int Taken { get; set; } = taken;
        public int PriceInCents { get; } = priceInCents;
        public DateTimeOffset StartsAt { get; } = startsAt;
        public DateTimeOffset EndsAt { get; } = endsAt;
        public CourseState State { get; set; } = state;
        public DateTimeOffset? PublishedAt { get; set; } = publishedAt;

        public CourseItem ToItem() => new(Id, Code, Title, TeacherId, Capacity, Math.Max(0, Capacity - Taken), PriceInCents, StartsAt, EndsAt,
                                          State, PublishedAt);
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

    public async Task<Result<CourseItem>> CreateAsync(string code, string title, int capacity, int priceInCents, DateTimeOffset startsAt,
                                                      DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Create(code, title, capacity, priceInCents, startsAt, endsAt);
    }

    public async Task<Result<CourseItem>> PublishAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Publish(courseId);
    }

    public async Task<Result<CourseItem>> CancelAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.CancelCourse(courseId);
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

    public async Task<Result<EnrollmentItem>> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return campus.Cancel(enrollmentId);
    }

    public async Task<Result<IReadOnlyList<ParticipantItem>>> ListParticipantsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Participants(courseId));
    }
}

internal sealed class DemoNotificationService(DemoCampus campus) : INotificationService
{
    public async Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Notifications());
    }

    public async Task<Result> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        campus.MarkAllRead();
        return Result.Ok();
    }
}

internal sealed class DemoAccountService(DemoCampus campus) : IAccountService
{
    public async Task<Result<IReadOnlyList<PaymentItem>>> ListPaymentsAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Payments());
    }

    public async Task<Result<IReadOnlyList<CertificateItem>>> ListCertificatesAsync(CancellationToken cancellationToken)
    {
        await campus.DelayAsync(cancellationToken);
        return Result.Ok(campus.Certificates());
    }
}

/// <summary>The demo's "server pushes": the in-memory campus raises them directly.</summary>
internal sealed class DemoLiveUpdates : ILiveUpdates
{
    public DemoLiveUpdates(DemoCampus campus)
    {
        campus.EnrollmentChanged += (_, change) => EnrollmentChanged?.Invoke(this, change);
        campus.NotificationReceived += (_, notification) => NotificationReceived?.Invoke(this, notification);
    }

    public event EventHandler<EnrollmentChange>? EnrollmentChanged;

    public event EventHandler<NotificationItem>? NotificationReceived;

    public bool IsConnected => true;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
        services.AddScoped<IAccountService, DemoAccountService>();
        services.AddSingleton<ILiveUpdates, DemoLiveUpdates>();
        return services;
    }
}
