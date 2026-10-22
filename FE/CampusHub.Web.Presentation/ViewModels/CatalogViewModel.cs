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
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<CourseItem> Courses { get; private set; }

    /// <summary>Courses for which an enrollment was requested during this visit.</summary>
    [ObservableProperty]
    public partial IReadOnlySet<Guid> RequestedCourseIds { get; private set; }

    [ObservableProperty]
    public partial string? InfoMessage { get; private set; }

    public bool IsEmpty => Courses.Count == 0;

    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    // Leaving the page cancels reads only. A request the user already sent is left to complete: cancelling it
    // half-way would leave them unsure whether they are enrolled (the server may have received it anyway).
    public override void Deactivate() => LoadCommand.Cancel();

    public bool WasRequested(CourseItem course) => course is not null && RequestedCourseIds.Contains(course.Id);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _catalog.ListPublishedAsync(cancellationToken);
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
        course is { State: CourseState.Published, IsFull: false } && !RequestedCourseIds.Contains(course.Id);
}
