using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Errors;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>
/// Catalogue of the courses open for enrollment; a student asks to join one.
/// <para>
/// The student's enrollments are loaded with the courses, so a course already joined shows it instead of offering
/// to enroll again. If they cannot be loaded, the courses are still shown: the Enrollment service refuses a
/// duplicate anyway.
/// </para>
/// <para>
/// The request id is generated once per course and kept until the server has accepted the request: a retry after
/// a timeout or a network error reuses it, so the Enrollment service never creates two enrollments for one click.
/// </para>
/// </summary>
public sealed partial class CatalogViewModel : ViewModelBase
{
    private readonly ICourseCatalog _catalog;
    private readonly IEnrollmentService _enrollments;
    private readonly Dictionary<Guid, Guid> _pendingRequestIds = [];

    public CatalogViewModel(ICourseCatalog catalog, IEnrollmentService enrollments)
    {
        _catalog = catalog;
        _enrollments = enrollments;
        Courses = [];
        RequestedCourseIds = new HashSet<Guid>();
        EnrollmentStates = new Dictionary<Guid, EnrollmentState>();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<CourseItem> Courses { get; private set; }

    /// <summary>Courses for which an enrollment was requested during this visit.</summary>
    [ObservableProperty]
    public partial IReadOnlySet<Guid> RequestedCourseIds { get; private set; }

    /// <summary>State of the student's latest enrollment in each course, rejected ones excluded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnrollCommand))]
    public partial IReadOnlyDictionary<Guid, EnrollmentState> EnrollmentStates { get; private set; }

    [ObservableProperty]
    public partial string? InfoMessage { get; private set; }

    public bool IsEmpty => Courses.Count == 0;

    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    // Leaving the page cancels reads only. A request the user already sent is left to complete: cancelling it
    // half-way would leave them unsure whether they are enrolled (the server may have received it anyway).
    public override void Deactivate() => LoadCommand.Cancel();

    public bool WasRequested(CourseItem course) => course is not null && RequestedCourseIds.Contains(course.Id);

    public CourseEnrollment EnrollmentOf(CourseItem course)
    {
        if (course is null)
        {
            return CourseEnrollment.None;
        }

        if (EnrollmentStates.TryGetValue(course.Id, out var state))
        {
            return state == EnrollmentState.Confirmed ? CourseEnrollment.Enrolled : CourseEnrollment.InProgress;
        }

        return RequestedCourseIds.Contains(course.Id) ? CourseEnrollment.InProgress : CourseEnrollment.None;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var courses = _catalog.ListPublishedAsync(cancellationToken);
        var enrollments = _enrollments.ListMineAsync(cancellationToken);
        await Task.WhenAll(courses, enrollments);

        var mine = await enrollments;
        if (mine.IsSuccess)
        {
            // Latest request first: a rejected one is ignored, the course can be requested again.
            EnrollmentStates = mine.Value.Where(e => e.State != EnrollmentState.Rejected)
                                         .GroupBy(e => e.CourseId)
                                         .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.RequestedAt).First().State);
        }

        var result = await courses;
        if (result.IsSuccess)
        {
            Courses = result.Value;
        }
        else
        {
            ShowErrors(result.Errors);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnroll))]
    private async Task EnrollAsync(CourseItem course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        ErrorMessage = null;
        InfoMessage = null;

        if (!_pendingRequestIds.TryGetValue(course.Id, out var requestId))
        {
            requestId = Guid.NewGuid();
            _pendingRequestIds[course.Id] = requestId;
        }

        var result = await _enrollments.RequestAsync(requestId, course.Id, cancellationToken);
        if (result.IsSuccess)
        {
            _pendingRequestIds.Remove(course.Id);
            RequestedCourseIds = new HashSet<Guid>(RequestedCourseIds) { course.Id };
            InfoMessage = $"Enrollment requested for \"{course.Title}\". Follow it in My enrollments.";
            return;
        }

        if (!result.Errors.AreTransient())
        {
            _pendingRequestIds.Remove(course.Id); // a definitive refusal: a new click is a new request
        }

        ShowErrors(result.Errors);
    }

    private bool CanEnroll(CourseItem? course) =>
        course is { State: CourseState.Published, IsFull: false } && EnrollmentOf(course) == CourseEnrollment.None;
}
