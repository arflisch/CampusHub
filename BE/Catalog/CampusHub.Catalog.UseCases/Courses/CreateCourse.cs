using Arc4u.Dependency.Attribute;
using Arc4u.Validation;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using FluentResults;
using FluentValidation;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

public sealed record CreateCourseCommand(string? Code, string? Title, int Capacity, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>Shape checks on the incoming request; the aggregate re-checks its own invariants.</summary>
public sealed class CreateCourseCommandValidator : AbstractValidator<CreateCourseCommand>
{
    public CreateCourseCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().Length(3, 20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Capacity).InclusiveBetween(1, Course.MaxCapacity);
        RuleFor(c => c.EndsAt).GreaterThan(c => c.StartsAt);
    }
}

/// <summary>A teacher creates a course in Draft. Only the teacher can publish it later.</summary>
[Export, Scoped]
public sealed class CreateCourse(ICourseRepository courses, TimeProvider timeProvider)
{
    private static readonly CreateCourseCommandValidator Validator = new();

    public async Task<Result<CourseDto>> ExecuteAsync(CreateCourseCommand command, string teacherId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await Validator.ValidateWithResultAsync(command, cancellationToken).ConfigureAwait(false);
        if (validation.IsFailed)
        {
            return validation.ToResult<CourseDto>();
        }

        var now = timeProvider.GetUtcNow();
        var course = Course.Create(Guid.CreateVersion7(now), command.Code, command.Title, command.Capacity,
                                   command.StartsAt, command.EndsAt, teacherId, now);
        if (course.IsFailed)
        {
            return course.ToResult<CourseDto>();
        }

        var added = await courses.AddAsync(course.Value, cancellationToken).ConfigureAwait(false);
        return added.IsFailed
            ? added.ToResult<CourseDto>()
            : Result.Ok(course.Value.ToDto(now));
    }
}
