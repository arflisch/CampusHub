using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>
/// A teacher's courses: create a course (Draft) and publish it. The form is validated as the user types; the server
/// re-checks everything (the aggregate owns the rules), its refusals are shown in the error banner.
/// <para>
/// Only teachers get the page: for anyone else nothing is loaded and the page explains how to become one (the
/// services refuse the calls anyway).
/// </para>
/// </summary>
public sealed partial class TeachingViewModel : ViewModelBase
{
    public const int MaxCapacity = 500;

    /// <summary>A course runs from the morning of its first day to the evening of its last day (UTC).</summary>
    public static readonly TimeOnly StartTime = new(9, 0);

    public static readonly TimeOnly EndTime = new(17, 0);

    private readonly ICourseCatalog _catalog;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    public TeachingViewModel(ICourseCatalog catalog, ICurrentUserService currentUser, TimeProvider timeProvider)
    {
        _catalog = catalog;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        MyCourses = [];
        Code = string.Empty;
        Title = string.Empty;
        Capacity = 30;
        (StartsOn, EndsOn) = DefaultSchedule();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<CourseItem> MyCourses { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Code { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapacityError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial int Capacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial DateOnly StartsOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial DateOnly EndsOn { get; set; }

    [ObservableProperty]
    public partial string? InfoMessage { get; private set; }

    /// <summary>Null until the user is known.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool? IsTeacher { get; private set; }

    /// <summary>Id of the signed-in user, to give to an administrator who grants the teacher role.</summary>
    [ObservableProperty]
    public partial string? UserId { get; private set; }

    public bool IsEmpty => MyCourses.Count == 0;

    public string? CodeError
    {
        get
        {
            var code = Code.Trim();
            if (Code.Length == 0)
            {
                return null; // nothing typed yet: no red message before the user starts
            }

            if (code.Length is < 3 or > 20)
            {
                return "Between 3 and 20 characters.";
            }

            return code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? null : "Letters, digits and dashes only.";
        }
    }

    public string? TitleError => Title.Length > 200 ? "200 characters at most." : null;

    public string? CapacityError => Capacity is < 1 or > MaxCapacity ? $"Between 1 and {MaxCapacity} seats." : null;

    public string? ScheduleError
    {
        get
        {
            if (StartsOn <= Today)
            {
                return "The course must start after today.";
            }

            if (EndsOn < StartsOn)
            {
                return "The last day comes after the first one.";
            }

            return EndsOn.DayNumber - StartsOn.DayNumber > 365 ? "A course lasts at most a year." : null;
        }
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetAsync(cancellationToken);
        if (user.IsFailed)
        {
            ShowErrors(user.Errors);
            return;
        }

        UserId = user.Value.UserId;
        IsTeacher = user.Value.IsTeacher;
        if (user.Value.IsTeacher)
        {
            await LoadCommand.ExecuteAsync(null);
        }
    }

    // Leaving the page cancels reads only: a creation or a publication the user sent is left to complete.
    public override void Deactivate() => LoadCommand.Cancel();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _catalog.ListMineAsync(cancellationToken);
        if (result.IsSuccess)
        {
            MyCourses = result.Value;
        }
        else
        {
            ShowErrors(result.Errors);
        }
    }

    private bool CanCreate() =>
        IsTeacher == true && Code.Length > 0 && CodeError is null && !string.IsNullOrWhiteSpace(Title) && TitleError is null && CapacityError is null
        && ScheduleError is null;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        InfoMessage = null;

        var startsAt = new DateTimeOffset(StartsOn.ToDateTime(StartTime), TimeSpan.Zero);
        var endsAt = new DateTimeOffset(EndsOn.ToDateTime(EndTime), TimeSpan.Zero);
        var result = await _catalog.CreateAsync(Code.Trim(), Title.Trim(), Capacity, startsAt, endsAt, cancellationToken);
        if (result.IsFailed)
        {
            ShowErrors(result.Errors);
            return;
        }

        MyCourses = [result.Value, .. MyCourses];
        InfoMessage = $"\"{result.Value.Title}\" was created as a draft. Publish it to open enrollments.";
        Code = string.Empty;
        Title = string.Empty;
        Capacity = 30;
        (StartsOn, EndsOn) = DefaultSchedule();
    }

    // Next week, for a month: a sensible starting point the teacher adjusts.
    private (DateOnly StartsOn, DateOnly EndsOn) DefaultSchedule() => (Today.AddDays(7), Today.AddDays(37));

    [RelayCommand]
    private async Task PublishAsync(CourseItem course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        ErrorMessage = null;
        InfoMessage = null;

        var result = await _catalog.PublishAsync(course.Id, cancellationToken);
        if (result.IsFailed)
        {
            ShowErrors(result.Errors);
            return;
        }

        MyCourses = [.. MyCourses.Select(c => c.Id == course.Id ? result.Value : c)];
        InfoMessage = $"\"{course.Title}\" is open for enrollment.";
    }
}
